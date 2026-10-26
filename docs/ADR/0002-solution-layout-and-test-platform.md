# 0002 — Solution layout, build policy and test platform

- Status: Accepted
- Date: 2026-10-25
- Task: P00-03

## Context
Guide §2/§3 fix the projects, dependency direction, MVVM + Generic Host, nullable/analyzers and pinned dependencies.
P00-03 left these choices open: solution format, target framework per project, test framework, and lock-file policy.

## Decision
1. **Solution:** `DynamicIsland.sln`, the classic format named in guide §3. It was created with `--format sln` because .NET 10 defaults to `.slnx`.
2. **Target frameworks:** `Core`, `Widgets` and `Infrastructure` target `net10.0`, so they stay portable and testable without Windows. `App` (WPF), `Platform.Windows`, `Launcher` and `Updater` target `net10.0-windows`. `Widgets` holds widget logic/view-models only, and XAML views live in `App`. WinRT for GSMTC media raised `Platform.Windows`, `App` and the Windows test projects to `net10.0-windows10.0.19041.0` (ADR 0020).
3. **Build policy (`Directory.Build.props`):** nullable on, warnings as errors, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`, deterministic builds, `ContinuousIntegrationBuild` on CI. `GenerateDocumentationFile` is on with CS1591 suppressed, so that IDE0005 is reported at build time.
4. **Dependencies:** central package management (`Directory.Packages.props`) with transitive pinning. `packages.lock.json` per project is committed, and CI restores in locked mode (`RestoreLockedMode` when `CI`/`GITHUB_ACTIONS`). `nuget.config` clears sources down to nuget.org, with package source mapping.
5. **Tests:** xUnit v3 (4.0.2) on **Microsoft.Testing.Platform**, enabled through `global.json` `test.runner`. The .NET 10 SDK `dotnet test` rejects MTP projects on the legacy VSTest path, so `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` are not used. Command: `dotnet test --solution DynamicIsland.sln -c Release`, or `--project <path>`.
6. **Composition:** `AppHost.CreateBuilder()` uses `DisableDefaults = true`. That means no command-line or environment configuration (launch arguments are untrusted input, guide §4.6) and no default log providers, including EventLog; the local file sink comes in P01. `ValidateOnBuild` and `ValidateScopes` are on.
7. **Architecture guard:** `DependencyBoundaryTests` checks every `src` project's references against an allow-list. It also checks that Core is `net10.0` with no UI frameworks or packages and has no Windows/WPF assembly references or `SupportedOSPlatform`.

## Alternatives considered
- `.slnx`: rejected for now, because guide §3 names `.sln`. Revisit when tooling support is universal.
- xUnit v2 / NUnit on VSTest: rejected. v2 is legacy, and staying on the VSTest path means fighting the .NET 10 SDK defaults.
- A minimal host built from DI only: rejected, because guide §2 specifies Generic Host.

## Consequences
- Every new package needs a pinned `PackageVersion` plus a `THIRD_PARTY_NOTICES.md` entry, and lock files must be regenerated and committed.
- The Windows smoke tests need an interactive desktop session. CI must run them on a Windows runner with a desktop session, or filter them by trait `Category=WindowsSmoke`.
