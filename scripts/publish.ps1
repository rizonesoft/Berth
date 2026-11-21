<#
.SYNOPSIS
Publishes Berth as a single self-contained win-x64 berth.exe into artifacts/ and writes its SHA-256 checksum.

.DESCRIPTION
Publishes into a fresh staging directory, verifies that berth.exe is the only file emitted (so disabling
PublishSingleFile fails loudly), writes the lowercase SHA-256 sidecar (<hex>  berth.exe), and only then
replaces artifacts/berth.exe and artifacts/berth.exe.sha256 so a failed publish never destroys the prior pair.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$root = Split-Path -Parent $PSScriptRoot
$artifactsDir = Join-Path $root 'artifacts'
$scratchBase = if (Test-Path -LiteralPath 'R:\') { 'R:\scratch' } else { [System.IO.Path]::GetTempPath() }
$stageRoot = Join-Path $scratchBase ("berth-publish-" + [Guid]::NewGuid().ToString('N'))
$publishDir = Join-Path $stageRoot 'out'

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
Push-Location $root
try {
    $bosun = Get-Command bosun -CommandType Application -ErrorAction Ignore | Select-Object -First 1
    $publishArgs = @('publish', 'src/Berth/Berth.csproj', '-c', 'Release', '-p:PublishProfile=Release', '-o', $publishDir)
    if ($bosun) {
        Write-Output "== publish: bosun run -- dotnet $($publishArgs -join ' ')"
        & $bosun.Source run -- dotnet @publishArgs
    }
    else {
        Write-Output "== publish: dotnet $($publishArgs -join ' ')"
        & dotnet @publishArgs
    }

    if ($LASTEXITCODE -ne 0) {
        [Console]::Error.WriteLine("publish: dotnet publish failed with exit code $LASTEXITCODE")
        exit $LASTEXITCODE
    }

    $emitted = @(Get-ChildItem -LiteralPath $publishDir -Recurse -File | Sort-Object FullName)
    if ($emitted.Count -ne 1 -or $emitted[0].Name -ne 'berth.exe' -or $emitted[0].DirectoryName -ne $publishDir) {
        $names = ($emitted | ForEach-Object { [System.IO.Path]::GetRelativePath($publishDir, $_.FullName) }) -join ', '
        [Console]::Error.WriteLine("publish: expected only berth.exe in publish output, found $($emitted.Count) file(s): $names")
        exit 1
    }

    $stagedExe = $emitted[0].FullName
    $hash = (Get-FileHash -LiteralPath $stagedExe -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumLine = "$hash  berth.exe`n"
    $stagedSha = Join-Path $stageRoot 'berth.exe.sha256'
    [System.IO.File]::WriteAllText($stagedSha, $checksumLine, [System.Text.UTF8Encoding]::new($false))

    New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
    $targetExe = Join-Path $artifactsDir 'berth.exe'
    $targetSha = Join-Path $artifactsDir 'berth.exe.sha256'
    $nextExe = Join-Path $artifactsDir 'berth.exe.next'
    $nextSha = Join-Path $artifactsDir 'berth.exe.sha256.next'
    $backupExe = Join-Path $artifactsDir 'berth.exe.prev'
    $backupSha = Join-Path $artifactsDir 'berth.exe.sha256.prev'

    Copy-Item -LiteralPath $stagedExe -Destination $nextExe -Force
    Copy-Item -LiteralPath $stagedSha -Destination $nextSha -Force

    $hadPrevExe = Test-Path -LiteralPath $targetExe
    $hadPrevSha = Test-Path -LiteralPath $targetSha
    if ($hadPrevExe) { Copy-Item -LiteralPath $targetExe -Destination $backupExe -Force }
    if ($hadPrevSha) { Copy-Item -LiteralPath $targetSha -Destination $backupSha -Force }

    try {
        # Move the sidecar first (if locked, neither target has changed yet), then move the exe.
        Move-Item -LiteralPath $nextSha -Destination $targetSha -Force
        Move-Item -LiteralPath $nextExe -Destination $targetExe -Force
    }
    catch {
        if ($hadPrevExe -and (Test-Path -LiteralPath $backupExe)) {
            Move-Item -LiteralPath $backupExe -Destination $targetExe -Force -ErrorAction SilentlyContinue
        }
        if ($hadPrevSha -and (Test-Path -LiteralPath $backupSha)) {
            Move-Item -LiteralPath $backupSha -Destination $targetSha -Force -ErrorAction SilentlyContinue
        }
        Remove-Item -LiteralPath $nextExe -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $nextSha -Force -ErrorAction SilentlyContinue
        throw
    }
    finally {
        Remove-Item -LiteralPath $backupExe -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $backupSha -Force -ErrorAction SilentlyContinue
    }

    # Remove any stale non-pair files in artifacts/ only after the new pair is safely in place.
    foreach ($extra in Get-ChildItem -LiteralPath $artifactsDir -Force) {
        if ($extra.Name -ne 'berth.exe' -and $extra.Name -ne 'berth.exe.sha256') {
            Remove-Item -LiteralPath $extra.FullName -Recurse -Force
        }
    }

    $sizeBytes = (Get-Item -LiteralPath $targetExe).Length
    $sizeMb = ($sizeBytes / 1MB).ToString('F2', [System.Globalization.CultureInfo]::InvariantCulture)
    Write-Output "Published artifacts/berth.exe ($sizeMb MB, sha256 $hash)"
    exit 0
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $stageRoot) {
        Remove-Item -LiteralPath $stageRoot -Recurse -Force
    }
}
