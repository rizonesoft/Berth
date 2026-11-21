#Requires -Version 7.0
[CmdletBinding()]
param(
    [switch]$RequireNative
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'Berth.slnx'
$docsDir = Join-Path $repoRoot 'docs'
$destDir = Join-Path $docsDir 'images'
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

    # Build a complete sibling directory and swap directories so both screenshots publish as one matched pair.
    New-Item -ItemType Directory -Path $docsDir -Force | Out-Null
    $nextDir = Join-Path $docsDir ("images.next-" + [Guid]::NewGuid().ToString('N'))
    $prevDir = Join-Path $docsDir ("images.prev-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $nextDir -Force | Out-Null

    if (Test-Path -LiteralPath $destDir) {
        foreach ($existing in Get-ChildItem -LiteralPath $destDir -Force) {
            if ($expected -notcontains $existing.Name) {
                Copy-Item -LiteralPath $existing.FullName -Destination (Join-Path $nextDir $existing.Name) -Recurse -Force
            }
        }
    }

    foreach ($name in $expected) {
        Copy-Item -LiteralPath (Join-Path $stageDir $name) -Destination (Join-Path $nextDir $name) -Force
    }

    $movedOld = $false
    try {
        if (Test-Path -LiteralPath $destDir) {
            Move-Item -LiteralPath $destDir -Destination $prevDir -Force
            $movedOld = $true
        }

        Move-Item -LiteralPath $nextDir -Destination $destDir -Force
    }
    catch {
        if ($movedOld -and -not (Test-Path -LiteralPath $destDir) -and (Test-Path -LiteralPath $prevDir)) {
            Move-Item -LiteralPath $prevDir -Destination $destDir -Force -ErrorAction SilentlyContinue
        }
        if (Test-Path -LiteralPath $nextDir) {
            Remove-Item -LiteralPath $nextDir -Recurse -Force -ErrorAction SilentlyContinue
        }
        throw
    }

    if (Test-Path -LiteralPath $prevDir) {
        Remove-Item -LiteralPath $prevDir -Recurse -Force -ErrorAction SilentlyContinue
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
