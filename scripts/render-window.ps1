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
        'map-unreachable-dark.png',
        'status-working-light.png',
        'status-working-dark.png',
        'status-restored-light.png',
        'status-restored-dark.png',
        'status-restored-partial-light.png',
        'status-restored-partial-dark.png',
        'status-undone-light.png',
        'status-undone-dark.png',
        'status-saved-light.png',
        'status-saved-dark.png',
        'status-saved-replaced-light.png',
        'status-saved-replaced-dark.png',
        'status-reverted-light.png',
        'status-reverted-dark.png',
        'status-refused-retry-light.png',
        'status-refused-retry-dark.png',
        'status-refused-no-retry-light.png',
        'status-refused-no-retry-dark.png',
        'status-in-place-light.png',
        'status-in-place-dark.png',
        'status-moved-and-missing-light.png',
        'status-moved-and-missing-dark.png',
        'status-moved-light.png',
        'status-moved-dark.png',
        'status-missing-light.png',
        'status-missing-dark.png',
        'status-unsaved-light.png',
        'status-unsaved-dark.png',
        'status-other-light.png',
        'status-other-dark.png',
        'notice-none-light.png',
        'notice-none-dark.png',
        'notice-auto-arrange-light.png',
        'notice-auto-arrange-dark.png',
        'notice-unreachable-light.png',
        'notice-unreachable-dark.png',
        'notice-unreadable-light.png',
        'notice-unreadable-dark.png',
        'notice-two-light.png',
        'notice-two-dark.png',
        'rename-valid-light.png',
        'rename-valid-dark.png',
        'rename-empty-light.png',
        'rename-empty-dark.png',
        'rename-too-long-light.png',
        'rename-too-long-dark.png',
        'rename-taken-light.png',
        'rename-taken-dark.png',
        'delete-dialog-light.png',
        'delete-dialog-dark.png',
        'switches-on-off-light.png',
        'switches-on-off-dark.png',
        'switches-off-on-light.png',
        'switches-off-on-dark.png'
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
