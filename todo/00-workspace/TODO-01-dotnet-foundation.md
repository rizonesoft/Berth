---
schema_version: 1
id: dotnet-foundation
domain: 00-workspace
status: active
title: "TODO-01 -- .NET foundation"
---

# TODO-01 -- .NET foundation

> **Goal:** The repository holds a .NET 11 solution with one application project and one test project that build and test with one command, locally and in CI, so every later section has somewhere to put code and a gate that can fail.

> **Current state:** Measured 2026-11-15. No solution, project or C# file exists. The repository holds the brand assets (`assets/`), the design contract (`design/SPEC.md`, `design/tokens.json`), the foundation plan (`docs/plans/2026-11-15-berth-foundation.md`), the Conclave kits (`base`, `todo`, `reviewer`) and one workflow, `.github/workflows/plan.yml`. `dotnet --version` prints `11.0.100` on the build machine.

## Inputs

- `docs/plans/2026-11-15-berth-foundation.md`: "Tech stack", "Console and window from one executable" and "Layout of the repository".
- `design/SPEC.md` §13 (the executable is a console program with a detached console policy).
- `assets/icon/berth.ico`: the application icon.
- -> XREF: D01 T01 §1 -- the core model is the first code built on this solution
- -> XREF: D02 T01 §1 -- the shell interop builds on the application project
- -> XREF: D03 T01 §1 -- the command line fills in the entry point made here
- -> XREF: D04 T01 §1 -- the application shell builds on the application project
- -> XREF: D07 T01 §1 -- packaging publishes the project made here

## Outcome

- `dotnet build Berth.slnx` and `dotnet test --solution Berth.slnx` succeed with warnings as errors.
- `pwsh scripts/check-all.ps1` runs every gate and stops at the first failure, and CI runs the same script on every push.

**Adjacency:** all=not-applicable (build tooling only: no records, settings or user-facing surface)

**Adjacency rationale:** This file creates the solution and its gates. Everything a user can see or change is owned by later files.

## Implementation Order

| Order | Section | Deliverable | Depends On | Status |
| ----- | ------- | ----------- | ---------- | ------ |
| 1 | §1 | Solution, SDK pin and shared build properties | -- | [x] |
| 2 | §2 | Application project that builds berth.exe | §1 | [x] |
| 3 | §3 | Test project with a first failing-capable test | §2 | [x] |
| 4 | §4 | One command that runs every gate | §3 | [x] |
| 5 | §5 | Build and test workflow in CI | §4 | [ ] |
| 6 | §6 | Test that keeps Core free of UI and COM types | §3 | [ ] |
| 7 | §7 | Review prompts that name the stamp-owned Commit item | -- | [x] |
| 8 | §8 | Script test scratch folder that falls back to R:\scratch | §7 | [x] |

## 1. Solution, SDK pin and shared build properties

> **Started:** 2026-11-18T00:21:00Z

Every project shares one SDK pin, one set of compiler settings and one place for package versions, so a later section never decides them again.

**Phase:** 0
**Needs:** none
**Surface:** no

**Build order:**

1. Pin the SDK in `global.json`. Done when: `dotnet --version` in the repository root prints an 11.0 SDK.
2. Add `Directory.Build.props`, `Directory.Packages.props` and `Berth.slnx`. Done when: `dotnet build Berth.slnx` exits 0 on the empty solution.

- [x] Create `global.json` with `sdk.version` `11.0.100`, `rollForward` `latestFeature`, `allowPrerelease` `false`, and `test.runner` `Microsoft.Testing.Platform`. Done when: `dotnet --version` run in the repository root prints a version starting `11.0.`, and reading `global.json` back with `ConvertFrom-Json` shows all four fields with exactly those values. **Corrected 2026-11-18:** the version check alone passed with no `global.json` on a machine whose newest SDK is 11.0.100 (plan review P1 1). Cheaper substitute: leaving the pin out, so the build silently uses whatever SDK is newest.
- [x] Create `Directory.Build.props` setting `LangVersion` `latest`, `Nullable` `enable`, `ImplicitUsings` `enable`, `TreatWarningsAsErrors` `true`, `AnalysisLevel` `latest-recommended`, `Deterministic` `true`, `InvariantGlobalization` `false` and `Version` `0.1.0`. Done when: `dotnet msbuild -getProperty:TreatWarningsAsErrors` on a project under `src/` prints `true` (checked in §2). Cheaper substitute: setting these in one project file, so the test project drifts.
- [x] Create `Directory.Packages.props` with `ManagePackageVersionsCentrally` `true` and no package yet. Done when: reading the file back as XML shows `ManagePackageVersionsCentrally` `true` and no `PackageVersion` item (the effective value on a real project is checked in §3). **Corrected 2026-11-18:** "the build reads it" passed with an empty `<Project />` (plan review P1 3). Cheaper substitute: versions written into each project file.
- [x] Create an empty solution with `dotnet new sln --format slnx --name Berth`. Done when: `Berth.slnx` exists in the repository root and `dotnet build Berth.slnx` exits 0. Cheaper substitute: a `.sln` file, which the sibling projects no longer use.
- [x] Commit: `build: solution, SDK pin and shared build properties`

**Test checkpoint:** `dotnet build Berth.slnx` exits 0 and the four `global.json` fields and the two props files read back as specified; in a scratch copy of the repository whose `global.json` names SDK `99.0.100`, `dotnet build Berth.slnx` exits non-zero with a message naming `99.0.100`. **Corrected 2026-11-18:** the negative check now runs on a scratch copy and must name the missing SDK (plan review P1 1).

> **Verified:** 2026-11-18 | §1 | 237a42ea538c8c91df3fda915ff5250f3a2a9efa
> **Review:** `docs/reviews/d00-t01-s1.json`

## 2. Application project that builds berth.exe

> **Started:** 2026-11-18T00:27:13Z

One project, as the foundation plan says, producing `berth.exe` as a console program with WPF and Windows Forms enabled and the manifest that keeps a console from appearing.

**Phase:** 0
**Needs:** none
**Surface:** no

**Build order:**

1. Create `src/Berth/Berth.csproj`, `src/Berth/app.manifest` and `src/Berth/Program.cs`, then add the project to `Berth.slnx` once. Done when: a clean `dotnet build Berth.slnx` produces `berth.exe`. **Corrected 2026-11-18:** the first build came before the entry point and manifest existed (plan review P1 1).
2. Run the built `berth.exe --version`. Done when: it prints `Berth 0.1.0`. **Corrected 2026-11-18:** step 1 now creates the manifest and entry point, so this step only runs the executable (round 1 P3 2).

- [x] Create `src/Berth/Berth.csproj` with `OutputType` `Exe`, `TargetFramework` `net11.0-windows`, `UseWPF` `true`, `UseWindowsForms` `true`, `AssemblyName` `berth`, `RootNamespace` `Berth`, `RuntimeIdentifier` `win-x64`, `ApplicationIcon` pointing at `..\..\assets\icon\berth.ico` and `ApplicationManifest` `app.manifest`. Done when: `dotnet msbuild src/Berth -getProperty:OutputType,TargetFramework,UseWPF,UseWindowsForms,AssemblyName,RootNamespace,RuntimeIdentifier,ApplicationIcon,ApplicationManifest` reads back all nine values, the built `berth.exe` is a PE32+ image for machine `0x8664` with subsystem 3 (Windows console, which is what makes a terminal wait), and each `RT_ICON` image its group icon names is byte for byte the image of the same size in `assets/icon/berth.ico` (round 1 P2 1 and P3 3: the bare `-getProperty` is refused with MSB1010, and comparing sizes alone passes stale artwork). **Corrected 2026-11-18:** "builds and holds berth.exe" passed with the frameworks off, another runtime or the default icon (plan review P1 2). Cheaper substitute: `OutputType` `WinExe`, which the foundation plan allows only as the recorded fallback of `D03 T01 §2`.
- [x] Add `src/Berth/app.manifest` declaring per-monitor DPI awareness version 2 (`PerMonitorV2`), the Windows 10 and 11 `supportedOS` id, `asInvoker` execution level, the version 6 common controls dependency, and `consoleAllocationPolicy` `detached` in the `windowsSettings` element under the namespace `http://schemas.microsoft.com/SMI/2024/WindowsSettings` (Microsoft Learn, "Console allocation policy": read the page and copy its manifest example's shape). Done when: the manifest extracted from the built apphost with `mt.exe '-inputresource:<path>\berth.exe;#1' '-out:<scratch>\m.xml'` (the argument quoted, since PowerShell ends a command at an unquoted semicolon) holds, by XML path and namespace, `requestedExecutionLevel/@level` `asInvoker`, the `supportedOS` id `{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}`, the `Microsoft.Windows.Common-Controls` 6.0.0.0 dependency, `windowsSettings/dpiAwareness` `PerMonitorV2` (2016 namespace) and `windowsSettings/consoleAllocationPolicy` `detached` (2024 namespace). **Corrected 2026-11-18:** the unquoted command lost its resource id and a text match let a misplaced element pass (plan review P2 4). Cheaper substitute: `requireAdministrator`, which Berth must never ask for (`SECURITY.md`).
- [x] Add `src/Berth/Program.cs` with a `[STAThread]` `Main(string[] args)` that prints `Berth ` followed by the assembly's informational version (without any `+commit` suffix) for `--version` and returns 0, and returns 2 for anything else for now. Done when: `berth.exe --version` writes exactly `Berth 0.1.0` and a newline to standard output, nothing to standard error, and exits 0; no arguments, `nosuch` and `--version extra` each exit 2; and the compiled `Main` carries `STAThreadAttribute`, read by reflection. **Corrected 2026-11-18:** a `Main` that ignored its arguments passed (plan review P1 3). Cheaper substitute: a hard-coded version string that will not follow `Directory.Build.props`.
- [x] Add the project to the solution with `dotnet sln Berth.slnx add src/Berth`. Done when: `dotnet build Berth.slnx` builds the project. Cheaper substitute: building the project file directly and leaving the solution empty.
- [x] Read the effective build properties of `src/Berth/Berth.csproj` with `dotnet msbuild src/Berth -getProperty:LangVersion -getProperty:Nullable -getProperty:ImplicitUsings -getProperty:TreatWarningsAsErrors -getProperty:AnalysisLevel -getProperty:Deterministic -getProperty:InvariantGlobalization -getProperty:Version`, and confirm none of the eight is set in the project file itself. Done when: the JSON output shows `latest`, `enable`, `enable`, `true`, `latest-recommended`, `true`, `false`, `0.1.0` and no `PropertyGroup` element in `src/Berth/Berth.csproj`, read as XML, has a child named after any of the eight (a text search would trip on the XML declaration's `version`; round 1 P3 of §1). **Corrected 2026-11-18:** added after plan review P1 2 of §1, since `TreatWarningsAsErrors` alone passed with the other seven missing. Cheaper substitute: setting the properties in the project file, so the test project drifts.
- [x] Add the folders `Core`, `Desktop`, `Cli`, `App`, `Window` and `Theme` under `src/Berth/` each with a `.gitkeep`. Done when: `git ls-files src/Berth` lists the six `.gitkeep` files. Cheaper substitute: leaving folder creation to whichever section comes first, so names drift from the foundation plan.
- [x] Commit: `build: application project that builds berth.exe`

**Test checkpoint:** a clean `dotnet build Berth.slnx` exits 0 and the built `berth.exe --version` prints exactly `Berth 0.1.0` with empty standard error; in a scratch copy of the repository with `Version` `0.2.0` in `Directory.Build.props`, the rebuilt `berth.exe --version` prints exactly `Berth 0.2.0` and exits 0. **Corrected 2026-11-18:** the edit now happens in a scratch copy and the output must be exact (plan review P2 5).

> **Verified:** 2026-11-18 | §2 | 0b9b45f8ec352e87fc5bb1e2483ea5e4e2b809dc
> **Review:** `docs/reviews/d00-t01-s2.json`

## 3. Test project with a first failing-capable test

> **Started:** 2026-11-18T00:35:16Z

The test project every later section adds to, with one real test so the runner is proven to fail when it should.

**Phase:** 0
**Needs:** none
**Surface:** no

**Corrected 2026-11-18:** the checklist below was rewritten after the plan review (P1 1 to 4, P2 5 and 6): `Microsoft.NET.Test.Sdk` is VSTest's package and xUnit v3 4.0.3 brings Microsoft.Testing.Platform v2 itself (`xunit.v3.core.mtp-v2`, confirmed in a scratch project on 2026-11-18); `AppInfo` must be what `Program` prints; the gate and `TestData` tests must prove what they claim.

**Build order:**

1. Create `tests/Berth.Tests/Berth.Tests.csproj` on xUnit v3 and Microsoft.Testing.Platform, with the project reference, `InternalsVisibleTo`, `AppInfo` and the first tests in place. Done when: `dotnet test --solution Berth.slnx` runs and reports the named tests passed.
2. Add the `TestData` helper and the `Desktop` collection. Done when: their behaviour tests pass. **Corrected 2026-11-18:** the first test run came before the test and the internal member it reads existed (plan review P1 4).

- [x] Add `xunit.v3` at its latest stable version (4.0.3 on 2026-11-18) to `Directory.Packages.props` as a `PackageVersion`. Done when: the file holds exactly that one `PackageVersion`. Cheaper substitute: a floating version (`*`), which makes builds unrepeatable.
- [x] Create `tests/Berth.Tests/Berth.Tests.csproj` targeting `net11.0-windows` with `OutputType` `Exe`, `UseWPF` `true`, a versionless `PackageReference` to `xunit.v3`, and a `ProjectReference` to `..\..\src\Berth\Berth.csproj`, and add it to `Berth.slnx`. Done when: `dotnet msbuild tests/Berth.Tests -getProperty:TargetFramework,OutputType,UseWPF,ManagePackageVersionsCentrally -getItem:ProjectReference` reads `net11.0-windows`, `Exe`, `true`, `true` and the one reference, and `dotnet list tests/Berth.Tests package` resolves `xunit.v3` `4.0.3`. Cheaper substitute: a test project that copies source files instead of referencing the application project.
- [x] Read the eight shared build properties of `tests/Berth.Tests/Berth.Tests.csproj` with `dotnet msbuild -getProperty:LangVersion,Nullable,ImplicitUsings,TreatWarningsAsErrors,AnalysisLevel,Deterministic,InvariantGlobalization,Version`, and confirm no `PackageReference` in it carries a `Version`. Done when: the values read as in §2 and the project file, read as XML, has no `PackageReference/@Version`. Cheaper substitute: a `Version` on each `PackageReference`.
- [x] Add `InternalsVisibleTo` for `Berth.Tests` in `src/Berth/Berth.csproj` (an `InternalsVisibleTo` item). Done when: a test that names the `internal` type `AppInfo` compiles. Cheaper substitute: making production types `public` only so tests can see them.
- [x] Add `internal static class AppInfo` in `src/Berth/Core/AppInfo.cs` whose `Version` property reads the assembly's informational version and drops any `+` suffix, and make `Program.cs` print `"Berth " + AppInfo.Version` with no version logic of its own. Done when: `Program.cs` no longer reads an assembly attribute, and in a scratch copy built with `Version` `0.2.0` and `SourceRevisionId` `abc123` (so the informational version is `0.2.0+abc123`) `berth.exe --version` prints exactly `Berth 0.2.0`. Cheaper substitute: a hard-coded `0.1.0`, which passes a pattern test.
- [x] Add `tests/Berth.Tests/AppInfoTests.cs` asserting `AppInfo.Version` matches `^\d+\.\d+\.\d+$` and equals the `Version` of `Directory.Build.props` read from the file at test time. Done when: both pass, and with the pattern changed to `^x$` the run reports an assertion failure in `AppInfoTests` (not a build error); restoring it is green again. Cheaper substitute: `Assert.True(true)`, which can never fail.
- [x] Add a `TestData` helper in `tests/Berth.Tests/TestData.cs` that creates a unique folder under the system temporary folder, exposes it as `Root`, and deletes the tree on dispose. It never changes the test process's environment, because xUnit runs test classes in parallel: in-process tests pass `Root` to the code under test (`BerthPaths.ForRoot` from `D01 T01 §3`), and tests that start a child process put `BERTH_DATA` in that child's own environment. Done when: `tests/Berth.Tests/TestDataTests.cs` keeps two instances alive at once, writes a marker file into each, finds the two roots different and both markers present, finds `BERTH_DATA` in the test process unchanged while both live, disposes one and finds only its tree gone, then disposes the other and finds it gone. Cheaper substitute: `Environment.SetEnvironmentVariable` in a test, which leaks into every test running at the same time.
- [x] Add `tests/Berth.Tests/DesktopCollection.cs`: `[CollectionDefinition("Desktop", DisableParallelization = true)]` with a `DesktopTestGate` collection fixture that takes the named mutex `Global\Berth.Tests.Desktop` in its constructor (default wait 10 minutes, a test-supplied wait for its own tests, an abandoned mutex counting as taken) and releases it on dispose, on the thread that took it. A wait that times out throws `another Berth interface test run holds Global\Berth.Tests.Desktop` and the tests in the collection do not run. Every test that touches the real desktop, opens a window, adds a tray icon or starts `berth.exe` is in this collection (`[Collection("Desktop")]`, rule in `AGENTS.md`), so such tests never run beside each other in one run, and a second test run on the same machine waits for the first. Done when: the gate tests below pass. **Corrected 2026-11-18:** added on the owner's instruction that interface and smoke tests must never clash. Cheaper substitute: `[assembly: CollectionBehavior(DisableTestParallelization = true)]`, which slows every unit test and still lets two runs clash.
- [x] Add `tests/Berth.Tests/DesktopTestGateTests.cs`, driving a helper process (the test assembly started with an argument, `CreateNoWindow`, a 20 second bound, the whole tree stopped in `finally`) that takes and holds the gate's mutex on command over standard input: (a) while a gate built in the test holds the mutex, the helper's try-acquire with zero wait fails, and after the gate is disposed the helper acquires it while the test process is still alive; (b) while the helper holds it, a gate with a 300 ms wait throws the stated message and a body guarded by it never runs; (c) after the helper is killed while holding it, a new gate acquires the abandoned mutex and releases it on dispose. Done when: all three pass and no helper process remains. Cheaper substitute: two test classes in one collection, which xUnit serialises even with an empty fixture.
- [x] Commit: `test: test project with a first failing-capable test`

**Test checkpoint:** `dotnet test --solution Berth.slnx` exits 0 with `AppInfoTests`, `TestDataTests` and `DesktopTestGateTests` passed by name; with `AppInfoTests` asserting `^x$` it exits non-zero reporting that assertion failure, and green again once restored. **Corrected 2026-11-18:** "at least 2 passed" and "any non-zero exit" passed with the gate tests missing or a build error (plan review P1 4).

> **Verified:** 2026-11-18 | §3 | 9fafd5965524c3e02b15f353f782ca9e23671e28
> **Review:** `docs/reviews/d00-t01-s3.json`

## 4. One command that runs every gate

> **Started:** 2026-11-18T00:52:38Z

`scripts/check-all.ps1` is the gate the rest of the plan names in its checkpoints: plan, tooling tests, agent tiers, build and tests, in that order, stopping at the first failure.

**Phase:** 0
**Needs:** none
**Surface:** no

**Corrected 2026-11-18:** rewritten after the plan review (P1 1 to 4, P2 5 to 7). The script's tests run its real step table against shim executables on a private `PATH`, which log every call, so the commands, arguments, order, stopping, exit codes, selection and Bosun routing are observed rather than inferred from the script's own labels. They live in `tests/scripts/`, the project's script-test folder, which becomes a seventh step, `script-tests`.

**Build order:**

1. Write `scripts/check-all.ps1` with named steps. Done when: `pwsh scripts/check-all.ps1` exits 0 on a clean tree.
2. Prove it fails and selects correctly. Done when: `python -m unittest discover -s tests/scripts` passes, and a deliberately broken test makes the real script exit non-zero naming the failing assertion.

- [x] Create `scripts/check-all.ps1` with the steps `plan-validate` (`python scripts/todo-graph.py validate`), `plan-current` (`python scripts/todo-graph.py plan --check`), `tooling-tests` (`python -m unittest discover -s scripts/tests`), `script-tests` (`python -m unittest discover -s tests/scripts`), `agent-tiers` (`node scripts/verify-agent-tiers.mjs`), `build` (`dotnet build Berth.slnx -c Release`) and `test` (`dotnet test --solution Berth.slnx -c Release`, with `--no-build` added only when `build` succeeded earlier in the same invocation), run in that order from the repository root, each printing `== <step>: <command line>` before it starts. Done when: `tests/scripts/test_check_all.py` runs the script against shims and finds exactly those seven command lines, in that order, with `-c Release` on both .NET commands and `--no-build` on `test`. Cheaper substitute: a script that prints step names it does not run.
- [x] Make the script stop at the first failing step, print `FAILED <step>: <command line> (exit <code>)` and exit with that code; a step whose executable is not found prints `FAILED <step>: <executable> not found` and exits 127. Done when: the shim test makes `agent-tiers` exit 7 and finds the script exit 7 with that exact line and no later command started, and with no `node` on the private `PATH` finds exit 127, the not-found line and no later command started. Cheaper substitute: `$ErrorActionPreference = 'Continue'`, which lets later steps hide the failure.
- [x] Add `-List` (print the seven step names, one per line, start nothing and exit 0) and `-Only <name[,name]>` (run just those steps in the declared order whatever order they are given in, refusing any unknown name with exit 2 before starting anything). Done when: the shim test finds `-List` prints the seven names and logs no call, `-Only build` runs only the build, `-Only test,plan-validate` runs `plan-validate` then `test` (without `--no-build`), and `-Only build,nosuch` exits 2 with no call logged. Cheaper substitute: asking callers to edit the script to skip a step.
- [x] Run `build` and `test` as `bosun run -- <command>` when `Get-Command bosun` finds it, and directly when it does not. Done when: the shim test with a logging `bosun` shim on the private `PATH` finds `bosun` received `run -- dotnet build Berth.slnx -c Release` and `run -- dotnet test --solution Berth.slnx -c Release --no-build`, and with no `bosun` on that `PATH` finds both `dotnet` commands logged directly. Cheaper substitute: requiring `bosun`, which a contributor's machine and CI do not have; or echoing a prefix without routing through it.
- [x] Add `tests/scripts/test_check_all.py` (Python `unittest`, standard library only) building the shims in a temporary folder: `python`, `node`, `dotnet` and `bosun` as `.cmd` files that append their name and arguments to a log, exit with a code taken from an environment variable keyed by step, and (for `bosun`) run the wrapped command; the script runs under `pwsh -NoProfile -File` with `PATH` set to the shim folder, the folder holding `pwsh`, and the Windows system folders only, and every child is bounded by a timeout with its process tree stopped on expiry. Done when: `python -m unittest discover -s tests/scripts` passes, and fails when `check-all.ps1` stops honouring `-Only`. Cheaper substitute: tests that grep the script's text.
- [x] Run the real script on the real tree. Done when: `pwsh scripts/check-all.ps1` exits 0; `-Only test` exits 0 on a clean tree; and in a scratch copy of the repository with `AppInfoTests.VersionIsThreeNumbers` asserting `^x$`, both the full script and `-Only test` exit non-zero, the output names `AppInfoTests.VersionIsThreeNumbers` and ends with `FAILED test:`, and the copy is green again once restored. Cheaper substitute: proving failure only through the shims.
- [x] Update `CONTRIBUTING.md`: under "What do I need?" name the SDK that `global.json` pins without the "once the solution exists" condition and add PowerShell 7 (`pwsh`); under "Which checks must pass?" make the first code block `pwsh scripts/check-all.ps1`, then show `-List` and `-Only build,test`, removing "Once the solution exists". Done when: the two sections read that way. Cheaper substitute: leaving the document describing a state that no longer exists.
- [x] Commit: `build: one command that runs every gate`

**Test checkpoint:** `pwsh scripts/check-all.ps1` exits 0 and `python -m unittest discover -s tests/scripts` passes; in a scratch copy with `AppInfoTests` asserting a wrong pattern, `pwsh scripts/check-all.ps1` exits non-zero, names `AppInfoTests.VersionIsThreeNumbers` and prints `FAILED test:`.

> **Verified:** 2026-11-18 | §4 | c3fd513f98cb07e5135c2dae0e2288c96e718e66
> **Review:** `docs/reviews/d00-t01-s4.json`

## 5. Build and test workflow in CI

The plan workflow already checks the TODO graph. This adds the build and the tests, so a push that breaks either is red.

**Phase:** 0
**Needs:** none
**Surface:** no

**Build order:**

1. Add `.github/workflows/build.yml` running `scripts/check-all.ps1` on `windows-latest`. Done when: the workflow run for the commit is green.
2. Add the badge and Dependabot coverage. Done when: `README.md` shows the Build badge and `.github/dependabot.yml` lists `nuget`.

- [ ] Create `.github/workflows/build.yml` named `build`, triggered on push to `main`, pull requests and `workflow_dispatch`, with `permissions: contents: read`, a `concurrency` group per ref, on `windows-latest` with a 30 minute timeout: `actions/checkout`, `actions/setup-dotnet` reading `global.json`, `actions/setup-python`, `actions/setup-node`, then `pwsh scripts/check-all.ps1`. Use the same action versions as `.github/workflows/plan.yml`. Done when: `python scripts/ci_status.py --workflow build.yml` reports the latest run on `main` as passed after the push. Cheaper substitute: `continue-on-error: true` on the test step.
- [ ] If .NET 11 is not installable on the hosted runner, the run must fail loudly, never skip: keep the `setup-dotnet` step strict. Done when: the workflow file holds no `if:` that skips the build and no `continue-on-error`. Cheaper substitute: a conditional that turns the job green when the SDK is missing.
- [ ] Add the Build badge to `README.md` before the Plan badge, in the same form. Done when: the README's badge row starts with the `build.yml` badge. Cheaper substitute: a static shields.io badge that always says passing.
- [ ] Add a `nuget` entry to `.github/dependabot.yml` (directory `/`, weekly, label `dependencies`). Done when: the file lists both `github-actions` and `nuget`. Cheaper substitute: none needed.
- [ ] Create `.conclave/ci.json` with `{"workflows": ["build.yml", "plan.yml"]}`. Done when: `python scripts/ci_status.py` lists exactly those two workflows. Cheaper substitute: leaving the watch list implicit, so a future workflow that is allowed to be red blocks the run.
- [ ] Commit: `ci: build and test workflow`

**Test checkpoint:** `python scripts/ci_status.py --workflow build.yml --workflow plan.yml` exits 0 with both green on `main` after the push, and exits 1 for a commit that breaks a test.

## 6. Test that keeps Core free of UI and COM types

The foundation plan's one architectural rule, enforced by a test instead of by convention.

**Phase:** 0
**Needs:** none
**Surface:** no

**Build order:**

1. Add a test that reads the compiled `berth` assembly and inspects every type in the `Berth.Core` namespace. Done when: it passes on the current tree.
2. Prove it fails. Done when: a temporary `Core` type with a `System.Windows.Window` field makes it fail.

- [ ] Add `tests/Berth.Tests/LayerRuleTests.cs` with a test that takes every type whose namespace is `Berth.Core` or starts with `Berth.Core.` and fails when any field, property, method parameter, return type or base type comes from an assembly named `PresentationFramework`, `PresentationCore`, `WindowsBase`, `System.Windows.Forms` or `System.Xaml`, or when the type carries `ComImportAttribute`. Done when: the test passes with `AppInfo` as the only Core type. Cheaper substitute: a code-review convention with no test.
- [ ] Make the failure message name the offending type, member and assembly. Done when: temporarily adding `internal sealed class Bad { System.Windows.Window? W; }` to `src/Berth/Core/` fails the test with a message containing `Berth.Core.Bad` and `PresentationFramework`, and removing it passes again. Cheaper substitute: a bare `Assert.Empty` with no explanation.
- [ ] Add a second test asserting at least one type exists in `Berth.Core`. Done when: it passes, and would fail if the namespace were renamed. Cheaper substitute: a rule test that passes vacuously on an empty set.
- [ ] Commit: `test: keep Core free of UI and COM types`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*LayerRuleTests"` passes, and fails while a `Berth.Core` type holds a `System.Windows.Window` field.

## 7. Review prompts that name the stamp-owned Commit item

> **Started:** 2026-11-18T01:11:58Z

Filed 2026-11-18 from the D00 T01 §4 reconciliation (P3): reviewers keep asking for the `- [ ] Commit:` item to be ticked before the stamp, but `stamp` ticks it (`todo/README.md`, "Stamps and receipts"), so every section draws the same finding. The prompts must say so.

**Phase:** 0
**Needs:** none
**Surface:** no

**Corrected 2026-11-18:** rewritten after the plan review (P1 1, P1 2, P2 3). `.conclave/kits.json` records the bytes the kit installed, so a Berth edit keeps those hashes and shows as local drift; the check reads the prompts `scripts/review-prompt.py` generates, since text above a template's `---` separator never reaches a reviewer; and the rule rides on each template's "TODO section" line, which the filler drops when no section is named. The upstream change belongs to the Conclave-SDK reviewer kit, whose checkout another session was writing on 2026-11-18; it is recorded as an owner item in the phase 0 run file.

**Build order:**

1. Append the rule to the "TODO section" line of each of the four review templates. Done when: each generated section prompt carries it once.
2. Test the generated prompts. Done when: `python -m unittest discover -s tests/scripts -p test_review_prompts.py` passes.

- [x] Append to the `- TODO section` line under "Candidate" in `docs/completion-review-prompt.md`, `docs/reconciliation-review-prompt.md`, `docs/review-round-prompt.md` and `docs/plan-review-prompt.md` the sentence: "Its `- [ ] Commit:` item and its Implementation Order row stay open until `stamp` ticks them after the last review, so an open `Commit:` item is not a gap." Done when: `Select-String -SimpleMatch` finds the sentence once in each template, on that line, below the `---` separator. Cheaper substitute: telling each reviewer in the per-section purpose text, which the next section forgets.
- [x] Leave the four files' `sha256` entries in `.conclave/kits.json` as installed. Done when: `git diff` shows no change to `.conclave/kits.json`. Cheaper substitute: rewriting the hashes to the edited bytes, which makes a local edit look like the installed baseline and lets a kit update overwrite it unseen.
- [x] Add `tests/scripts/test_review_prompts.py`, which runs `scripts/review-prompt.py` for each kind with `--ref "D00 T01 §1"` (and the arguments each kind requires) into a temporary folder, asserts the sentence appears exactly once in each generated prompt, and runs `--kind round` without `--ref` and asserts it is absent. Done when: it passes; and in a scratch copy where the sentence is moved above the completion template's `---` separator, the completion assertion fails. Cheaper substitute: reading the template files, which passes while reviewers never see the sentence.
- [x] Commit: `review: prompts name the stamp-owned Commit item`

**Test checkpoint:** `python -m unittest discover -s tests/scripts -p test_review_prompts.py` passes, and fails in a scratch copy whose completion template holds the sentence above its `---` separator.

> **Verified:** 2026-11-18 | §7 | aaf3ec4cf3c5788b4cd3b92e8dceb96cc8f8d315
> **Review:** `docs/reviews/d00-t01-s7.json`

## 8. Script test scratch folder that falls back to R:\scratch

> **Started:** 2026-11-18T01:21:36Z

Filed 2026-11-18 from the D00 T01 §7 completion review (P3): `tests/scripts/support.py` `scratch_root()` reads `paths.scratch` from `.conclave/local.toml` and otherwise uses the operating system's temporary folder, but `AGENTS.md` ("Working rules") names `R:\scratch` on Windows, and its test compares the folder with the same helper that chose it.

**Phase:** 0
**Needs:** none
**Surface:** no

**Corrected 2026-11-18:** rewritten after the plan review (P1 1, P2 2 and 3, P3 4). A supplied session scratchpad comes first, passed as `BERTH_SCRATCH` like the sibling projects' `BOSUN_SCRATCH` and `OFFICE_SCRATCH`; one check runs the production defaults against an expected folder computed without `scratch_root()`; the configured folder is created when missing; and the temporary-folder case expects `Path(tempfile.gettempdir())`, not a machine literal.

**Build order:**

1. Give `scratch_root()` its four-step order with injectable inputs. Done when: each step wins in its case.
2. Test every case and the production defaults. Done when: `python -m unittest discover -s tests/scripts -p test_support.py` passes.

- [x] Make `scratch_root(environ=os.environ, local_file=<repo>/.conclave/local.toml, candidate=Path(r"R:\scratch"))` in `tests/scripts/support.py` choose, in order: `BERTH_SCRATCH` from `environ` when set (the session scratchpad); `paths.scratch` from `local_file` when set; `candidate` when that folder exists; `tempfile.gettempdir()` otherwise. A chosen `BERTH_SCRATCH` or `paths.scratch` folder is created with its parents; `candidate` is never created. Done when: the tests below pass. Cheaper substitute: creating `R:\scratch` when it is missing, which writes to a drive the machine may not have.
- [x] Add tests to `tests/scripts/test_support.py`, each with a `local.toml`, candidate and session folder of its own under a temporary folder: `BERTH_SCRATCH` wins over a configured `paths.scratch` and an existing candidate, and `make_scratch` creates its folder there; a configured `paths.scratch` naming a missing nested folder wins over an existing candidate, the folder is created, and `make_scratch` succeeds in it; with neither, an existing candidate wins; with nothing, the result is `Path(tempfile.gettempdir())` and the missing candidate is still absent. Done when: all pass, and the candidate test fails when that step is removed. Cheaper substitute: comparing the result with another call of `scratch_root()`.
- [x] Add a test of the production defaults: with `BERTH_SCRATCH` removed from `environ` and `local_file` pointing at a missing file, `make_scratch` with no other argument creates its folder under `Path(r"R:\scratch")` when that folder exists and under `Path(tempfile.gettempdir())` when it does not, the expected value computed in the test without `scratch_root()`. Done when: it passes, and fails when the default candidate is misspelled (`R:\scrath`) on a machine that has `R:\scratch`. Cheaper substitute: only testing with injected folders, so a typo in the default passes.
- [x] Commit: `test: script test scratch folder falls back to R:\scratch`

**Test checkpoint:** `python -m unittest discover -s tests/scripts -p test_support.py` passes, and fails when `scratch_root()` skips the `R:\scratch` step or misspells its default.

> **Verified:** 2026-11-18 | §8 | b2128b8aec374f7497c3d97a1d225ec3ea05b82d
> **Review:** `docs/reviews/d00-t01-s8.json`

## Verification

- A fresh clone builds and passes with `pwsh scripts/check-all.ps1`, and both workflows are green on `main`.
- `python scripts/todo-graph.py validate` and `python scripts/todo-graph.py plan --check` pass.
