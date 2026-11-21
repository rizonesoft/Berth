#Requires -Version 7.0
[CmdletBinding()]
param(
    [switch]$RequireNative,
    [switch]$PublishOnly,
    [string]$StageDir,
    [string]$DestDir
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'Berth.slnx'
$resolvedDestDir = if ([string]::IsNullOrWhiteSpace($DestDir)) { Join-Path $repoRoot 'docs\images' } else { $DestDir }
$scratchBase = if (Test-Path -LiteralPath 'R:\') { 'R:\scratch' } else { [System.IO.Path]::GetTempPath() }
$ownsStageDir = [string]::IsNullOrWhiteSpace($StageDir)
$resolvedStageDir = if ($ownsStageDir) { Join-Path $scratchBase ("berth-docs-images-" + [Guid]::NewGuid().ToString('N')) } else { $StageDir }

if ($ownsStageDir) {
    New-Item -ItemType Directory -Path $resolvedStageDir -Force | Out-Null
}
$previousDir = $env:BERTH_DOCS_IMAGE_DIR
$previousNative = $env:BERTH_DOCS_REQUIRE_NATIVE
Push-Location $repoRoot
try {
    if (-not $PublishOnly) {
        $env:BERTH_DOCS_IMAGE_DIR = $resolvedStageDir
        if ($RequireNative) {
            $env:BERTH_DOCS_REQUIRE_NATIVE = '1'
        }
        elseif (Test-Path Env:BERTH_DOCS_REQUIRE_NATIVE) {
            Remove-Item Env:BERTH_DOCS_REQUIRE_NATIVE
        }

        dotnet test --solution $solution -c Release --filter-trait Category=Docs | Out-Host
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }

    $expected = @('main-window-light.png', 'main-window-dark.png')
    foreach ($name in $expected) {
        $stagedFile = Join-Path $resolvedStageDir $name
        if (-not (Test-Path -LiteralPath $stagedFile) -or (Get-Item -LiteralPath $stagedFile).Length -le 0) {
            throw "Expected docs screenshot '$stagedFile' was not produced by this run."
        }
    }

    New-Item -ItemType Directory -Path $resolvedDestDir -Force | Out-Null
    New-Item -ItemType Directory -Path $scratchBase -Force | Out-Null
    $destFull = [System.IO.Path]::GetFullPath($resolvedDestDir).TrimEnd('\', '/').ToLowerInvariant()
    $destHash = [System.BitConverter]::ToString(
        [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($destFull))
    ).Replace('-', '').Substring(0, 16)
    # Cross-session exclusive lock file in scratch: works across interactive and non-interactive Windows sessions,
    # requires no SeCreateGlobalPrivilege, and is released automatically by the OS kernel if the process is killed.
    $lockPath = Join-Path $scratchBase "berth-docs-images-$destHash.lock"
    $lockStream = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ($null -eq $lockStream) {
        try {
            $lockStream = [System.IO.FileStream]::new(
                $lockPath,
                [System.IO.FileMode]::OpenOrCreate,
                [System.IO.FileAccess]::ReadWrite,
                [System.IO.FileShare]::None
            )
        }
        catch [System.IO.IOException] {
            if ([DateTime]::UtcNow -ge $deadline) {
                throw "Timed out waiting for docs image publish lock '$lockPath' for '$resolvedDestDir'."
            }
            Start-Sleep -Milliseconds 50
        }
    }

    try {
        # Clean up orphaned staging or backup files from any previously terminated publisher.
        foreach ($name in $expected) {
            foreach ($orphan in (Get-ChildItem -LiteralPath $resolvedDestDir -Filter "$name.*.next" -File)) {
                Remove-Item -LiteralPath $orphan.FullName -Force
            }
            foreach ($orphan in (Get-ChildItem -LiteralPath $resolvedDestDir -Filter "$name.*.prev" -File)) {
                Remove-Item -LiteralPath $orphan.FullName -Force
            }
        }

        $runId = [Guid]::NewGuid().ToString('N').Substring(0, 12)
        $nextPaths = @{}
        $backupPaths = @{}
        foreach ($name in $expected) {
            $dst = Join-Path $resolvedDestDir $name
            $next = "$dst.$runId.next"
            Copy-Item -LiteralPath (Join-Path $resolvedStageDir $name) -Destination $next -Force
            $nextPaths[$name] = $next
            $backupPaths[$name] = "$dst.$runId.prev"
        }

        # Use File.Replace (Win32 ReplaceFileW) under the cross-session lock so canonical paths never disappear.
        $replaced = @()
        try {
            foreach ($name in $expected) {
                $dst = Join-Path $resolvedDestDir $name
                if (Test-Path -LiteralPath $dst) {
                    [System.IO.File]::Replace($nextPaths[$name], $dst, $backupPaths[$name], $true)
                }
                else {
                    Move-Item -LiteralPath $nextPaths[$name] -Destination $dst -Force
                }
                $replaced += $name

                if ($replaced.Count -eq 1 -and -not [string]::IsNullOrWhiteSpace($env:BERTH_DOCS_TEST_PAUSE_AFTER_FIRST)) {
                    Set-Content -LiteralPath $env:BERTH_DOCS_TEST_PAUSE_AFTER_FIRST -Value 'paused' -Encoding ascii
                    for ($i = 0; $i -lt 200; $i++) {
                        Start-Sleep -Milliseconds 50
                    }
                }
            }
        }
        catch {
            foreach ($name in $replaced) {
                $dst = Join-Path $resolvedDestDir $name
                if (Test-Path -LiteralPath $backupPaths[$name]) {
                    [System.IO.File]::Replace($backupPaths[$name], $dst, $null, $true)
                }
            }
            foreach ($name in $expected) {
                if (Test-Path -LiteralPath $nextPaths[$name]) {
                    Remove-Item -LiteralPath $nextPaths[$name] -Force
                }
            }
            throw
        }
        finally {
            foreach ($name in $expected) {
                if (Test-Path -LiteralPath $backupPaths[$name]) {
                    Remove-Item -LiteralPath $backupPaths[$name] -Force
                }
            }
        }
    }
    finally {
        $lockStream.Dispose()
    }

    $modesFile = Join-Path $resolvedStageDir 'capture-modes.txt'
    if (Test-Path -LiteralPath $modesFile) {
        Get-Content -LiteralPath $modesFile | ForEach-Object { Write-Output "capture: $_" }
    }

    foreach ($name in $expected) {
        Write-Output (Join-Path $resolvedDestDir $name)
    }
}
finally {
    Pop-Location
    if ($null -eq $previousDir) {
        if (Test-Path Env:BERTH_DOCS_IMAGE_DIR) {
            Remove-Item Env:BERTH_DOCS_IMAGE_DIR
        }
    }
    else {
        $env:BERTH_DOCS_IMAGE_DIR = $previousDir
    }
    if ($null -eq $previousNative) {
        if (Test-Path Env:BERTH_DOCS_REQUIRE_NATIVE) {
            Remove-Item Env:BERTH_DOCS_REQUIRE_NATIVE
        }
    }
    else {
        $env:BERTH_DOCS_REQUIRE_NATIVE = $previousNative
    }

    if ($ownsStageDir -and (Test-Path -LiteralPath $resolvedStageDir)) {
        Remove-Item -LiteralPath $resolvedStageDir -Recurse -Force
    }
}
