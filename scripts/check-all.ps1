<#
.SYNOPSIS
Runs every gate Berth's plan names, in order, and stops at the first failure.

.DESCRIPTION
The steps are the TODO graph, the generated plan, the plan and review tooling tests, the project's script tests,
the Claude agent tiers, the Release build and the tests. Each step prints "== <step>: <command line>" before it
starts. A failing step prints "FAILED <step>: <command line> (exit <code>)" and the script exits with that code; a
step whose program is not installed prints "FAILED <step>: <program> not found" and exits 127.

The build and the tests run through "bosun run --" when Bosun is on PATH, so heavy jobs on a shared machine take
turns, and directly when it is not. The tests reuse the build (--no-build) only when the build step succeeded in
the same run, so "-Only test" never reports on an old binary.

.PARAMETER List
Print the step names, one per line, run nothing and exit 0.

.PARAMETER Only
Run just these steps (comma-separated or an array), in the declared order. An unknown name exits 2 before any step
starts.

.EXAMPLE
pwsh scripts/check-all.ps1

.EXAMPLE
pwsh scripts/check-all.ps1 -Only build,test
#>
[CmdletBinding()]
param(
    [switch]$List,
    [string[]]$Only,
    [switch]$Published
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# The gate reads each program's exit code itself, so a caller's preference must not turn a non-zero exit into a
# terminating error before the FAILED line is printed.
$PSNativeCommandUseErrorActionPreference = $false

$steps = @(
    @{ Name = 'plan-validate'; Program = 'python'; Arguments = @('scripts/todo-graph.py', 'validate') }
    @{ Name = 'plan-current'; Program = 'python'; Arguments = @('scripts/todo-graph.py', 'plan', '--check') }
    @{ Name = 'tooling-tests'; Program = 'python'; Arguments = @('-m', 'unittest', 'discover', '-s', 'scripts/tests') }
    @{ Name = 'script-tests'; Program = 'python'; Arguments = @('-m', 'unittest', 'discover', '-s', 'tests/scripts') }
    @{ Name = 'agent-tiers'; Program = 'node'; Arguments = @('scripts/verify-agent-tiers.mjs') }
    @{ Name = 'tokens'; Program = 'python'; Arguments = @('scripts/build-tokens.py', '--check') }
    @{ Name = 'build'; Program = 'dotnet'; Arguments = @('build', 'Berth.slnx', '-c', 'Release'); Heavy = $true }
    @{ Name = 'test'; Program = 'dotnet'; Arguments = @('test', '--solution', 'Berth.slnx', '-c', 'Release', '--filter-not-trait', 'Category=Published'); Heavy = $true; ReusesBuild = $true }
)
$names = $steps | ForEach-Object { $_.Name }

if ($List) {
    $names | ForEach-Object { Write-Output $_ }
    exit 0
}

$selected = $names
if ($PSBoundParameters.ContainsKey('Only')) {
    $wanted = @($Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $unknown = @($wanted | Where-Object { $names -notcontains $_ })
    if ($wanted.Count -eq 0 -or $unknown.Count -gt 0) {
        [Console]::Error.WriteLine("check-all: unknown step '$($unknown -join "', '")'. Steps: $($names -join ', ')")
        exit 2
    }

    $selected = @($names | Where-Object { $wanted -contains $_ })
}

$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
$bosun = Get-Command bosun -CommandType Application -ErrorAction Ignore | Select-Object -First 1
$built = $false

foreach ($step in $steps | Where-Object { $selected -contains $_.Name }) {
    $arguments = @($step.Arguments)
    if ($step.ContainsKey('ReusesBuild') -and $built) {
        $arguments += '--no-build'
    }

    $program = Get-Command $step.Program -CommandType Application -ErrorAction Ignore | Select-Object -First 1
    if (-not $program) {
        Write-Output "FAILED $($step.Name): $($step.Program) not found"
        exit 127
    }

    $line = (@($step.Program) + $arguments) -join ' '
    if ($step.ContainsKey('Heavy') -and $bosun) {
        $line = "bosun run -- $line"
        Write-Output "== $($step.Name): $line"
        & $bosun.Source run -- $step.Program @arguments
    }
    else {
        Write-Output "== $($step.Name): $line"
        & $program.Source @arguments
    }

    $code = $LASTEXITCODE
    if ($code -ne 0) {
        Write-Output "FAILED $($step.Name): $line (exit $code)"
        exit $code
    }

    if ($step.Name -eq 'build') {
        $built = $true
    }
}

if ($Published) {
    Write-Output '== publish: pwsh scripts/publish.ps1'
    & pwsh (Join-Path $PSScriptRoot 'publish.ps1')
    if ($LASTEXITCODE -ne 0) {
        Write-Output "FAILED publish: pwsh scripts/publish.ps1 (exit $LASTEXITCODE)"
        exit $LASTEXITCODE
    }

    $publishedExe = Join-Path $root 'artifacts\berth.exe'
    if (-not (Test-Path -LiteralPath $publishedExe)) {
        Write-Output "FAILED publish: expected $publishedExe to exist"
        exit 1
    }

    $previousBerthExe = $env:BERTH_EXE
    $env:BERTH_EXE = (Resolve-Path -LiteralPath $publishedExe).Path
    try {
        $publishedSuites = @(
            @{ Name = 'published-artifact'; Trait = 'Category=Published'; AllowSkipped = $false }
            @{ Name = 'published-real-desktop'; Trait = 'Category=RealDesktop'; AllowSkipped = $true }
            @{ Name = 'published-smoke'; Trait = 'Category=Smoke'; AllowSkipped = $false }
        )

        foreach ($suite in $publishedSuites) {
            $suiteArgs = @('test', '--solution', 'Berth.slnx', '-c', 'Release', '--filter-trait', $suite.Trait)
            if ($built) {
                $suiteArgs += '--no-build'
            }

            $line = (@('dotnet') + $suiteArgs) -join ' '
            if ($bosun) {
                $line = "bosun run -- $line"
                Write-Output "== $($suite.Name): $line"
                $output = & $bosun.Source run -- dotnet @suiteArgs 2>&1
            }
            else {
                Write-Output "== $($suite.Name): $line"
                $output = & dotnet @suiteArgs 2>&1
            }

            $code = $LASTEXITCODE
            $output | ForEach-Object { Write-Output $_ }
            if ($code -ne 0) {
                Write-Output "FAILED $($suite.Name): $line (exit $code)"
                exit $code
            }

            $joined = ($output | Out-String)
            $totalMatch = [regex]::Match($joined, 'total:\s*(\d+)')
            $skippedMatch = [regex]::Match($joined, 'skipped:\s*(\d+)')
            if (-not $totalMatch.Success -or [int]$totalMatch.Groups[1].Value -le 0) {
                Write-Output "FAILED $($suite.Name): filter '$($suite.Trait)' selected 0 tests"
                exit 1
            }

            if (-not $suite.AllowSkipped -and $skippedMatch.Success -and [int]$skippedMatch.Groups[1].Value -gt 0) {
                Write-Output "FAILED $($suite.Name): required published suite skipped $($skippedMatch.Groups[1].Value) test(s)"
                exit 1
            }
        }
    }
    finally {
        if ($null -eq $previousBerthExe) {
            if (Test-Path Env:BERTH_EXE) {
                Remove-Item Env:BERTH_EXE
            }
        }
        else {
            $env:BERTH_EXE = $previousBerthExe
        }
    }
}

Write-Output "check-all: $($selected.Count) step(s) passed"
exit 0
