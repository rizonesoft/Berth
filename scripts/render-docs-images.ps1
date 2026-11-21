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
        $src = Join-Path $stageDir $name
        $dst = Join-Path $destDir $name
        $next = "$dst.next"
        $backup = "$dst.prev"
        Copy-Item -LiteralPath $src -Destination $next -Force
        $nextPaths[$name] = $next
        if (Test-Path -LiteralPath $dst) {
            Copy-Item -LiteralPath $dst -Destination $backup -Force
            $backupPaths[$name] = $backup
        }
    }

    try {
        foreach ($name in $expected) {
            $dst = Join-Path $destDir $name
            Move-Item -LiteralPath $nextPaths[$name] -Destination $dst -Force
        }
    }
    catch {
        foreach ($name in $expected) {
            $dst = Join-Path $destDir $name
            if ($backupPaths.ContainsKey($name) -and (Test-Path -LiteralPath $backupPaths[$name])) {
                Move-Item -LiteralPath $backupPaths[$name] -Destination $dst -Force -ErrorAction SilentlyContinue
            }
            if ($nextPaths.ContainsKey($name)) {
                Remove-Item -LiteralPath $nextPaths[$name] -Force -ErrorAction SilentlyContinue
            }
        }
        throw
    }
    finally {
        foreach ($name in $expected) {
            if ($backupPaths.ContainsKey($name)) {
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
