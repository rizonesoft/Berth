#Requires -Version 7.0
[CmdletBinding()]
param(
    [switch]$RequireNative
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'Berth.slnx'
$destDir = Join-Path $repoRoot 'docs\images'
$scratchBase = if (Test-Path -LiteralPath 'R:\') { 'R:\scratch' } else { [System.IO.Path]::GetTempPath() }
$stageDir = Join-Path $scratchBase ("berth-docs-images-" + [Guid]::NewGuid().ToString('N'))

New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
$previousDir = $env:BERTH_DOCS_IMAGE_DIR
$previousNative = $env:BERTH_DOCS_REQUIRE_NATIVE
Push-Location $repoRoot
try {
    $env:BERTH_DOCS_IMAGE_DIR = $stageDir
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

    $expected = @('main-window-light.png', 'main-window-dark.png')
    foreach ($name in $expected) {
        $stagedFile = Join-Path $stageDir $name
        if (-not (Test-Path -LiteralPath $stagedFile) -or (Get-Item -LiteralPath $stagedFile).Length -le 0) {
            throw "Expected docs screenshot '$stagedFile' was not produced by this run."
        }
    }

    New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    $nextPaths = @{}
    $backupPaths = @{}
    foreach ($name in $expected) {
        $dst = Join-Path $destDir $name
        $next = "$dst.next"
        Copy-Item -LiteralPath (Join-Path $stageDir $name) -Destination $next -Force
        $nextPaths[$name] = $next
        $backupPaths[$name] = "$dst.prev"
    }

    # Use File.Replace (Win32 ReplaceFileW) so canonical docs/images/*.png paths never disappear even if interrupted.
    $replaced = @()
    try {
        foreach ($name in $expected) {
            $dst = Join-Path $destDir $name
            if (Test-Path -LiteralPath $dst) {
                [System.IO.File]::Replace($nextPaths[$name], $dst, $backupPaths[$name], $true)
            }
            else {
                Move-Item -LiteralPath $nextPaths[$name] -Destination $dst -Force
            }
            $replaced += $name
        }
    }
    catch {
        foreach ($name in $replaced) {
            $dst = Join-Path $destDir $name
            if (Test-Path -LiteralPath $backupPaths[$name]) {
                [System.IO.File]::Replace($backupPaths[$name], $dst, $null, $true)
            }
        }
        foreach ($name in $expected) {
            if (Test-Path -LiteralPath $nextPaths[$name]) {
                Remove-Item -LiteralPath $nextPaths[$name] -Force -ErrorAction SilentlyContinue
            }
        }
        throw
    }
    finally {
        foreach ($name in $expected) {
            if (Test-Path -LiteralPath $backupPaths[$name]) {
                Remove-Item -LiteralPath $backupPaths[$name] -Force -ErrorAction SilentlyContinue
            }
        }
    }

    $modesFile = Join-Path $stageDir 'capture-modes.txt'
    if (Test-Path -LiteralPath $modesFile) {
        Get-Content -LiteralPath $modesFile | ForEach-Object { Write-Output "capture: $_" }
    }

    foreach ($name in $expected) {
        Write-Output (Join-Path $destDir $name)
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

    if (Test-Path -LiteralPath $stageDir) {
        Remove-Item -LiteralPath $stageDir -Recurse -Force
    }
}
