#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'Berth.slnx'
$destDir = Join-Path $repoRoot 'build\renders'
$stageDir = Join-Path ([System.IO.Path]::GetTempPath()) ("berth-renders-" + [Guid]::NewGuid().ToString('N'))

New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
$previousRenderDir = $env:BERTH_RENDER_DIR
Push-Location $repoRoot
try {
    $env:BERTH_RENDER_DIR = $stageDir
    dotnet test --solution $solution -c Release --filter-trait Category=Render | Out-Host
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    $expectedNames = @(
        'frame-light.png',
        'frame-dark.png',
        'layouts-none-light.png',
        'layouts-none-dark.png',
        'layouts-one-light.png',
        'layouts-one-dark.png',
        'layouts-several-light.png',
        'layouts-several-dark.png',
        'layouts-active-saved-light.png',
        'layouts-active-saved-dark.png',
        'layouts-active-unsaved-light.png',
        'layouts-active-unsaved-dark.png',
        'header-active-light.png',
        'header-active-dark.png',
        'header-unsaved-light.png',
        'header-unsaved-dark.png',
        'header-other-light.png',
        'header-other-dark.png',
        'header-busy-light.png',
        'header-busy-dark.png',
        'map-monitors-light.png',
        'map-monitors-dark.png',
        'map-in-place-light.png',
        'map-in-place-dark.png',
        'map-moved-light.png',
        'map-moved-dark.png',
        'map-missing-light.png',
        'map-missing-dark.png',
        'map-selected-light.png',
        'map-selected-dark.png',
        'map-none-selected-light.png',
        'map-none-selected-dark.png',
        'map-neutral-light.png',
        'map-neutral-dark.png',
        'map-off-screen-light.png',
        'map-off-screen-dark.png',
        'map-unreachable-light.png',
        'map-unreachable-dark.png'
    )
    foreach ($name in $expectedNames) {
        $stagedFile = Join-Path $stageDir $name
        if (-not (Test-Path -LiteralPath $stagedFile) -or (Get-Item -LiteralPath $stagedFile).Length -le 0) {
            throw "Expected rendered image '$stagedFile' was not produced by this run."
        }
    }

    New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    $produced = Get-ChildItem -LiteralPath $stageDir -Filter '*.png' | Sort-Object Name
    foreach ($file in $produced) {
        $target = Join-Path $destDir $file.Name
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        Write-Output $target
    }
}
finally {
    Pop-Location
    if ($null -eq $previousRenderDir) {
        if (Test-Path Env:BERTH_RENDER_DIR) {
            Remove-Item Env:BERTH_RENDER_DIR
        }
    }
    else {
        $env:BERTH_RENDER_DIR = $previousRenderDir
    }

    if (Test-Path -LiteralPath $stageDir) {
        Remove-Item -LiteralPath $stageDir -Recurse -Force
    }
}
