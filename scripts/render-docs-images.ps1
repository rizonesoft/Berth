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

if ($RequireNative -and $PublishOnly) {
    throw '-RequireNative cannot be combined with -PublishOnly.'
}

Add-Type -AssemblyName PresentationCore
if (-not ('BerthDocsPathHelper' -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

public static class BerthDocsPathHelper
{
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_SHARE_ALL = 0x00000007;
    private const uint OPEN_EXISTING = 3;
    private const uint VOLUME_NAME_GUID = 0x1;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle hFile,
        StringBuilder lpszFilePath,
        uint cchFilePath,
        uint dwFlags);

    public static string GetCanonicalDirectoryKey(string directoryPath)
    {
        using SafeFileHandle handle = CreateFileW(
            directoryPath,
            0,
            FILE_SHARE_ALL,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return Path.GetFullPath(directoryPath).TrimEnd('\\', '/').ToLowerInvariant();
        }
        var sb = new StringBuilder(1024);
        uint len = GetFinalPathNameByHandleW(handle, sb, (uint)sb.Capacity, VOLUME_NAME_GUID);
        if (len == 0 || len >= sb.Capacity)
        {
            return Path.GetFullPath(directoryPath).TrimEnd('\\', '/').ToLowerInvariant();
        }
        return sb.ToString().TrimEnd('\\', '/').ToLowerInvariant();
    }
}
"@
}

function Test-ValidDocsPng {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf) -or (Get-Item -LiteralPath $Path).Length -le 0) {
        throw "Expected docs screenshot '$Path' was not produced by this run."
    }
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $decoder = [System.Windows.Media.Imaging.PngBitmapDecoder]::new(
            $stream,
            [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
            [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad
        )
        if ($decoder.Frames.Count -lt 1) {
            throw "Docs screenshot '$Path' contains no PNG frames."
        }
        $frame = $decoder.Frames[0]
        if ($frame.PixelWidth -ne 980 -or $frame.PixelHeight -ne 640) {
            throw "Docs screenshot '$Path' must be 980x640, got $($frame.PixelWidth)x$($frame.PixelHeight)."
        }
        $bgra = if ($frame.Format -eq [System.Windows.Media.PixelFormats]::Bgra32) {
            $frame
        }
        else {
            [System.Windows.Media.Imaging.FormatConvertedBitmap]::new($frame, [System.Windows.Media.PixelFormats]::Bgra32, $null, 0)
        }
        $stride = $bgra.PixelWidth * 4
        $pixels = New-Object byte[] ($stride * $bgra.PixelHeight)
        $bgra.CopyPixels($pixels, $stride, 0)

        # Verify the right-hand position-map pane (x=290..920, y=175..525) is populated with icon dots, not blank.
        $distinct = [System.Collections.Generic.HashSet[int]]::new()
        $accentDots = 0
        for ($y = 175; $y -lt 525; $y += 2) {
            $rowOffset = $y * $stride
            for ($x = 290; $x -lt 920; $x += 2) {
                $idx = $rowOffset + ($x * 4)
                $b = [int]$pixels[$idx]
                $g = [int]$pixels[$idx + 1]
                $r = [int]$pixels[$idx + 2]
                $null = $distinct.Add(($r -shl 16) -bor ($g -shl 8) -bor $b)
                if ($b -gt ($r + 40) -and $b -gt 140) {
                    $accentDots++
                }
            }
        }
        if ($distinct.Count -lt 6 -or $accentDots -lt 40) {
            throw "Docs screenshot '$Path' appears to have a blank or unpopulated position map (distinct=$($distinct.Count), accentDots=$accentDots)."
        }
    }
    finally {
        $stream.Dispose()
    }
}

$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$solution = Join-Path $repoRoot 'Berth.slnx'
$resolvedDestDir = if ([string]::IsNullOrWhiteSpace($DestDir)) {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'docs\images'))
}
else {
    [System.IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DestDir))
}

$scratchBase = if (Test-Path -LiteralPath 'R:\') { 'R:\scratch' } else { [System.IO.Path]::GetTempPath() }
$ownsStageDir = [string]::IsNullOrWhiteSpace($StageDir)
$resolvedStageDir = if ($ownsStageDir) {
    [System.IO.Path]::GetFullPath((Join-Path $scratchBase ("berth-docs-images-" + [Guid]::NewGuid().ToString('N'))))
}
else {
    [System.IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($StageDir))
}

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
    New-Item -ItemType Directory -Path $resolvedDestDir -Force | Out-Null
    $lockDir = Join-Path ([System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::CommonApplicationData)) 'Berth\locks'
    New-Item -ItemType Directory -Path $lockDir -Force | Out-Null
    # Resolve via GetFinalPathNameByHandleW (VOLUME_NAME_GUID) so subst/junction/symlink aliases share one lock.
    $destFull = [BerthDocsPathHelper]::GetCanonicalDirectoryKey($resolvedDestDir)
    $destHash = [System.BitConverter]::ToString(
        [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($destFull))
    ).Replace('-', '').Substring(0, 16)
    # Machine-wide exclusive lock file under ProgramData with DeleteOnClose so the OS kernel removes it on close/kill.
    $lockPath = Join-Path $lockDir "berth-docs-images-$destHash.lock"
    $lockStream = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ($null -eq $lockStream) {
        try {
            $lockStream = [System.IO.FileStream]::new(
                $lockPath,
                [System.IO.FileMode]::OpenOrCreate,
                [System.IO.FileAccess]::ReadWrite,
                [System.IO.FileShare]::None,
                4096,
                [System.IO.FileOptions]::DeleteOnClose
            )
        }
        catch [System.IO.IOException], [System.UnauthorizedAccessException] {
            if (-not [string]::IsNullOrWhiteSpace($env:BERTH_DOCS_TEST_LOCK_WAIT_SIGNAL) -and -not (Test-Path -LiteralPath $env:BERTH_DOCS_TEST_LOCK_WAIT_SIGNAL)) {
                Set-Content -LiteralPath $env:BERTH_DOCS_TEST_LOCK_WAIT_SIGNAL -Value 'waiting' -Encoding ascii
            }
            if ([DateTime]::UtcNow -ge $deadline) {
                throw "Timed out waiting for docs image publish lock '$lockPath' for '$resolvedDestDir'."
            }
            Start-Sleep -Milliseconds 50
        }
    }

    try {
        # Clean up orphaned staging or backup files from any previously terminated publisher (12-hex run IDs only).
        foreach ($name in $expected) {
            $orphanPattern = '^' + [regex]::Escape($name) + '\.[0-9a-f]{12}\.(next|prev)$'
            foreach ($orphan in (Get-ChildItem -LiteralPath $resolvedDestDir -File | Where-Object { $_.Name -match $orphanPattern })) {
                Remove-Item -LiteralPath $orphan.FullName -Force
            }
        }

        $runId = [Guid]::NewGuid().ToString('N').Substring(0, 12)
        $nextPaths = @{}
        $backupPaths = @{}
        # Stage private .next copies under the lock, validate those exact bytes, then replace via File.Replace.
        $replaced = @()
        $createdWithoutPrior = @{}
        try {
            foreach ($name in $expected) {
                $stagedFile = Join-Path $resolvedStageDir $name
                if (-not (Test-Path -LiteralPath $stagedFile -PathType Leaf)) {
                    throw "Expected docs screenshot '$stagedFile' was not produced by this run."
                }
                $dst = Join-Path $resolvedDestDir $name
                $next = "$dst.$runId.next"
                Copy-Item -LiteralPath $stagedFile -Destination $next -Force
                $nextPaths[$name] = $next
                $backupPaths[$name] = "$dst.$runId.prev"
            }

            foreach ($name in $expected) {
                Test-ValidDocsPng -Path $nextPaths[$name]
            }

            foreach ($name in $expected) {
                $dst = Join-Path $resolvedDestDir $name
                if (Test-Path -LiteralPath $dst) {
                    $createdWithoutPrior[$name] = $false
                    [System.IO.File]::Replace($nextPaths[$name], $dst, $backupPaths[$name], $true)
                }
                else {
                    $createdWithoutPrior[$name] = $true
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
                elseif ($createdWithoutPrior[$name] -and (Test-Path -LiteralPath $dst)) {
                    Remove-Item -LiteralPath $dst -Force
                }
            }
            foreach ($name in $expected) {
                if ($nextPaths.ContainsKey($name) -and (Test-Path -LiteralPath $nextPaths[$name])) {
                    Remove-Item -LiteralPath $nextPaths[$name] -Force
                }
            }
            throw
        }
        finally {
            foreach ($name in $expected) {
                if ($backupPaths.ContainsKey($name) -and (Test-Path -LiteralPath $backupPaths[$name])) {
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
