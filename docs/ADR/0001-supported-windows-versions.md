# 0001 — Supported Windows versions for v1

- Status: Accepted
- Date: 2026-10-25
- Task: P00-02

## Context
Guide §0 targets "Windows 11 and compatible Windows 10 installations" on .NET 10 LTS and forbids implying a generic
Windows 10 support guarantee. P00-02 requires recording the actual .NET 10 Windows support matrix.

Official sources, read 2026-10-25:
- `dotnet/core` `release-notes/10.0/supported-os.md` (last updated 2026-09-28): .NET 10 is LTS, phase Active.
  Windows client rows: **11 26H1, 11 25H2, 11 24H2 (IoT), 11 24H2 (E), 11 24H2, 11 23H2 (E), 10 21H2 (E), 10 21H2 (IoT),
  10 1809 (E), 10 1607 (E)**. Architectures: Arm64, x64, x86. Out of support: 11 23H2 (W) since 2025-11-11.
  Windows 10 22H2 consumer (Home/Pro) is **not listed**. The page mentions no Windows 10 ESU.
- .NET support policy page (updated 2026-10-13): .NET 10 end of support is **2028-11-14**. Latest patch is 10.0.13 (2026-10-13).
- `releases.json`: latest SDK 10.0.402, latest runtime 10.0.13 (security release).

The page does not define "(E)" or "(IoT)". We read "(E)" as Enterprise/Education. Reading the 1809 and 1607 rows as LTSC builds is also our inference.

## Decision
1. Runtime stays on .NET 10 LTS (`net10.0-windows`). `global.json` pins SDK `10.0.401` with `rollForward: latestFeature` and no prereleases.
2. **v1 tested target:** Windows 11 24H2 / 25H2 (and 26H1 when available), x64.
3. **Runtime-supported, not yet validated:** Windows 11 23H2 Enterprise/Education. Windows 10 21H2 Enterprise/IoT. Windows 10 1809 and 1607 Enterprise. These are listed as "may work" until a P07 compatibility pass validates them.
4. **Not supported:** Windows 10 22H2 Home/Pro and any OS that .NET 10 does not support. Docs, installer and About must not claim them.
5. Arm64 stays a post-x64 evaluation, per guide §0.

## Alternatives considered
- Claim "Windows 10 and 11": rejected, because it misrepresents both .NET and Microsoft lifecycle support.
- Multi-target .NET 8: rejected. .NET 8 reaches end of support on 2026-11-10, and the guide freezes .NET 10.

## Consequences
- README, installer and About must state this matrix. P07 adds a compatibility-validation row per OS.
- Hosts should install SDK 10.0.402 / runtime 10.0.13, the 2026-10-13 security release, before release builds. CI (P00-04) uses the latest 10.0.x SDK through `global.json` roll-forward.
- Re-check `supported-os.md` before each release; it changes over time.
