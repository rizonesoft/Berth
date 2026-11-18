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
| 2 | §2 | Application project that builds berth.exe | §1 | [ ] |
| 3 | §3 | Test project with a first failing-capable test | §2 | [ ] |
| 4 | §4 | One command that runs every gate | §3 | [ ] |
| 5 | §5 | Build and test workflow in CI | §4 | [ ] |
| 6 | §6 | Test that keeps Core free of UI and COM types | §3 | [ ] |

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

One project, as the foundation plan says, producing `berth.exe` as a console program with WPF and Windows Forms enabled and the manifest that keeps a console from appearing.

**Phase:** 0
**Needs:** none
**Surface:** no

**Build order:**

1. Create `src/Berth/Berth.csproj` and add it to `Berth.slnx`. Done when: `dotnet build Berth.slnx` produces `berth.exe`.
2. Add `app.manifest` and a `Program.cs` that only prints the version. Done when: the built `berth.exe --version` prints `Berth 0.1.0`.

- [ ] Create `src/Berth/Berth.csproj` with `OutputType` `Exe`, `TargetFramework` `net11.0-windows`, `UseWPF` `true`, `UseWindowsForms` `true`, `AssemblyName` `berth`, `RootNamespace` `Berth`, `RuntimeIdentifier` `win-x64`, `ApplicationIcon` pointing at `..\..\assets\icon\berth.ico` and `ApplicationManifest` `app.manifest`. Done when: `dotnet build src/Berth` exits 0 and the output folder holds `berth.exe`. Cheaper substitute: `OutputType` `WinExe`, which the foundation plan allows only as the recorded fallback of `D03 T01 §2`.
- [ ] Add `src/Berth/app.manifest` declaring per-monitor DPI awareness version 2 (`PerMonitorV2`), the Windows 10 and 11 `supportedOS` id, `asInvoker` execution level, the version 6 common controls dependency, and `consoleAllocationPolicy` `detached` in the `windowsSettings` element under the namespace `http://schemas.microsoft.com/SMI/2024/WindowsSettings` (Microsoft Learn, "Console allocation policy": read the page and copy its manifest example's shape). Done when: `mt.exe -inputresource:berth.exe;#1 -out:CON` or reading the embedded manifest with a PE reader in a test shows all five entries. Cheaper substitute: `requireAdministrator`, which Berth must never ask for (`SECURITY.md`).
- [ ] Add `src/Berth/Program.cs` with a `[STAThread]` `Main(string[] args)` that prints `Berth ` followed by the assembly's informational version (without any `+commit` suffix) for `--version` and returns 0, and returns 2 for anything else for now. Done when: running the built `berth.exe --version` prints exactly `Berth 0.1.0` and exits 0. Cheaper substitute: a hard-coded version string that will not follow `Directory.Build.props`.
- [ ] Add the project to the solution with `dotnet sln Berth.slnx add src/Berth`. Done when: `dotnet build Berth.slnx` builds the project. Cheaper substitute: building the project file directly and leaving the solution empty.
- [ ] Read the effective build properties of `src/Berth/Berth.csproj` with `dotnet msbuild src/Berth -getProperty:LangVersion -getProperty:Nullable -getProperty:ImplicitUsings -getProperty:TreatWarningsAsErrors -getProperty:AnalysisLevel -getProperty:Deterministic -getProperty:InvariantGlobalization -getProperty:Version`, and confirm none of the eight is set in the project file itself. Done when: the JSON output shows `latest`, `enable`, `enable`, `true`, `latest-recommended`, `true`, `false`, `0.1.0` and no `PropertyGroup` element in `src/Berth/Berth.csproj`, read as XML, has a child named after any of the eight (a text search would trip on the XML declaration's `version`; round 1 P3 of §1). **Corrected 2026-11-18:** added after plan review P1 2 of §1, since `TreatWarningsAsErrors` alone passed with the other seven missing. Cheaper substitute: setting the properties in the project file, so the test project drifts.
- [ ] Add the folders `Core`, `Desktop`, `Cli`, `App`, `Window` and `Theme` under `src/Berth/` each with a `.gitkeep`. Done when: `git ls-files src/Berth` lists the six `.gitkeep` files. Cheaper substitute: leaving folder creation to whichever section comes first, so names drift from the foundation plan.
- [ ] Commit: `build: application project that builds berth.exe`

**Test checkpoint:** `dotnet build Berth.slnx` exits 0 and the built `berth.exe --version` prints `Berth 0.1.0`; changing `Version` in `Directory.Build.props` to `0.2.0` changes the output.

## 3. Test project with a first failing-capable test

The test project every later section adds to, with one real test so the runner is proven to fail when it should.

**Phase:** 0
**Needs:** none
**Surface:** no

**Build order:**

1. Create `tests/Berth.Tests/Berth.Tests.csproj` on xUnit v3 and Microsoft.Testing.Platform. Done when: `dotnet test --solution Berth.slnx` runs and reports one passed test.
2. Give tests access to internals. Done when: a test calls an `internal` member of `berth`.

- [ ] Add `xunit.v3` and `Microsoft.NET.Test.Sdk` (and the xUnit v3 runner package Microsoft.Testing.Platform needs, per the xUnit v3 documentation for the installed version) to `Directory.Packages.props` at their latest stable versions. Done when: `dotnet restore Berth.slnx` exits 0. Cheaper substitute: a floating version (`*`), which makes builds unrepeatable.
- [ ] Create `tests/Berth.Tests/Berth.Tests.csproj` targeting `net11.0-windows` with `UseWPF` `true`, `OutputType` `Exe`, a project reference to `src/Berth/Berth.csproj`, and add it to `Berth.slnx`. Done when: `dotnet build Berth.slnx` builds both projects. Cheaper substitute: a test project that copies source files instead of referencing the application project.
- [ ] Add `InternalsVisibleTo` for `Berth.Tests` in `src/Berth/Berth.csproj`. Done when: a test that names an `internal` type compiles. Cheaper substitute: making production types `public` only so tests can see them.
- [ ] Read the effective `ManagePackageVersionsCentrally` and the eight shared build properties of `tests/Berth.Tests/Berth.Tests.csproj` with `dotnet msbuild -getProperty`, and confirm its package references carry no `Version` attribute. Done when: `ManagePackageVersionsCentrally` reads `true`, the eight properties read as in §2, and `dotnet restore Berth.slnx` resolves each package to the version in `Directory.Packages.props`. **Corrected 2026-11-18:** added after plan review P1 2 and P1 3 of §1. Cheaper substitute: a `Version` on each `PackageReference`.
- [ ] Move the version text into an `internal static class AppInfo` in `src/Berth/Core/AppInfo.cs` with a `Version` property, used by `Program.cs`. Done when: `berth.exe --version` still prints `Berth 0.1.0`. Cheaper substitute: leaving the logic in `Program.cs`, where a test cannot reach it.
- [ ] Add `tests/Berth.Tests/AppInfoTests.cs` asserting `AppInfo.Version` matches `^\d+\.\d+\.\d+$`. Done when: `dotnet test --solution Berth.slnx` reports 1 passed, and reports 1 failed when the pattern is changed to `^x$`. Cheaper substitute: `Assert.True(true)`, which can never fail.
- [ ] Add a `TestData` helper in `tests/Berth.Tests/TestData.cs` that creates a unique temporary folder, exposes it as `Root`, and deletes it on dispose. It never changes the test process's environment, because xUnit runs test classes in parallel: in-process tests pass `Root` to the code under test (`BerthPaths.ForRoot` from `D01 T01 §3`), and tests that start a child process put `BERTH_DATA` in that child's own environment. Done when: two tests using it in parallel get different folders, each folder is gone after its test, and `Environment.GetEnvironmentVariable("BERTH_DATA")` in the test process is unchanged. Cheaper substitute: `Environment.SetEnvironmentVariable` in a test, which leaks into every test running at the same time.
- [ ] Add `tests/Berth.Tests/DesktopCollection.cs`: `[CollectionDefinition("Desktop", DisableParallelization = true)]` with a `DesktopTestGate` collection fixture that takes the named mutex `Global\Berth.Tests.Desktop` (waiting up to 10 minutes, an abandoned mutex counting as taken) and releases it on dispose, on the thread that took it. Every test that touches the real desktop, opens a window, adds a tray icon or starts `berth.exe` is in this collection (`[Collection("Desktop")]`), so such tests never run beside each other in one run, and a second test run on the same machine (another session, CI beside a local run) waits for the first. A wait that times out fails the test with `another Berth interface test run holds Global\Berth.Tests.Desktop`. Done when: two test classes in the collection, each recording its start and end time, never overlap, and a helper process holding the mutex makes a collection test wait until it releases. **Corrected 2026-11-18:** added on the owner's instruction that interface and smoke tests must never clash. Cheaper substitute: `[assembly: CollectionBehavior(DisableTestParallelization = true)]`, which slows every unit test and still lets two runs clash.
- [ ] Commit: `test: test project with a first failing-capable test`

**Test checkpoint:** `dotnet test --solution Berth.slnx` exits 0 with at least 2 tests passed, and exits non-zero when `AppInfoTests` is made to assert a wrong pattern.

## 4. One command that runs every gate

`scripts/check-all.ps1` is the gate the rest of the plan names in its checkpoints: plan, tooling tests, agent tiers, build and tests, in that order, stopping at the first failure.

**Phase:** 0
**Needs:** none
**Surface:** no

**Build order:**

1. Write `scripts/check-all.ps1` with named steps. Done when: `pwsh scripts/check-all.ps1` exits 0 on a clean tree.
2. Prove it fails. Done when: a deliberately broken test makes it exit non-zero and name the failing step.

- [ ] Create `scripts/check-all.ps1` with the steps `plan-validate` (`python scripts/todo-graph.py validate`), `plan-current` (`python scripts/todo-graph.py plan --check`), `tooling-tests` (`python -m unittest discover -s scripts/tests`), `agent-tiers` (`node scripts/verify-agent-tiers.mjs`), `build` (`dotnet build Berth.slnx -c Release`) and `test` (`dotnet test --solution Berth.slnx -c Release --no-build`), run in that order. Done when: `pwsh scripts/check-all.ps1` prints each step's name and exits 0. Cheaper substitute: a script that runs only the build.
- [ ] Make the script stop at the first failing step, print `FAILED <step>: <command> (exit <code>)` and exit with that code. Done when: with a failing test in the tree, the output ends with `FAILED test:` and the exit code is non-zero. Cheaper substitute: `$ErrorActionPreference = 'Continue'`, which lets later steps hide the failure.
- [ ] Add `-List` (print the step names and exit 0) and `-Only <name[,name]>` (run just those steps, refusing an unknown name with exit 2). Done when: `pwsh scripts/check-all.ps1 -List` prints the six names and `-Only nosuch` exits 2. Cheaper substitute: asking callers to edit the script to skip a step.
- [ ] Run `build` and `test` through `bosun run --` when `Get-Command bosun` finds it, and directly when it does not. Done when: the script's output shows the `bosun run --` prefix on the build machine and the script still passes with `bosun` renamed off `PATH`. Cheaper substitute: requiring `bosun`, which a contributor's machine and CI do not have.
- [ ] Update `CONTRIBUTING.md` under "Which checks must pass?" to name `pwsh scripts/check-all.ps1` as the one command, removing "Once the solution exists". Done when: the section's first code block is that command. Cheaper substitute: leaving the document describing a state that no longer exists.
- [ ] Commit: `build: one command that runs every gate`

**Test checkpoint:** `pwsh scripts/check-all.ps1` exits 0; with `AppInfoTests` asserting a wrong pattern it exits non-zero and prints `FAILED test:`.

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

## Verification

- A fresh clone builds and passes with `pwsh scripts/check-all.ps1`, and both workflows are green on `main`.
- `python scripts/todo-graph.py validate` and `python scripts/todo-graph.py plan --check` pass.
