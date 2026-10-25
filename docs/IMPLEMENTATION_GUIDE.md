# Dynamic Island for Windows — Agent-Executable Implementation Guide

**Document version:** 1.0.0 | **Baseline date:** 2026-10-25 | **Status:** Approved requirements / not implemented

> This is a build specification, not evidence that the software exists. Execute sequentially. **Before every session, read `AGENTS.md` and `docs/AGENT_PROGRESS.md`; do not restart completed phases.** After every verified task, persist progress. Never claim an untested Windows behavior is working.

## 0. Product contract (non-negotiable)

Build a native, accessible, high-performance, customizable **Windows desktop Dynamic Island** with a compact top-of-screen floating pill, click-to-expand dashboard and hover preview. It must be **free, open source, offline-first, no mandatory paid API, account or server**, work as a **standard user**, and use **C# + WPF/XAML + Win32/WinRT**. Target Windows 11 and compatible Windows 10 installations; actual OS support must be verified. First deliver **x64**; evaluate ARM64 subsequently. Use current supported **.NET 10 LTS** (`net10.0-windows`) unless verification at implementation time requires a change, record reasons in an ADR. Note Windows 10 ordinary Home/Pro versions may be out of Microsoft support; the .NET Windows support matrix covers specific Enterprise/LTSC configurations. Do not misrepresent Microsoft lifecycle support.

**Frozen product decisions:**

| ID | Decision | Required behavior |
|---|---|---|
| D01 | Portable EXE + per-user installer | Both release formats; neither assumes admin access |
| D02 | Start with Windows | Ask on first launch; per-user sign-in startup only |
| D03 | Crash recovery | At most 2 automatic restarts per 5 minutes; then Safe Mode/stop |
| D04 | Activity analytics | Opt-in and entirely local; post-v1 module |
| D05 | Multiple monitors | User selects monitor; primary fallback |
| D06 | Fullscreen | Auto-hide for fullscreen apps/games; manual override in settings |
| D07 | Input behavior | Click expands; hover previews; explicit collapse |
| D08 | Appearance | Themes, size, opacity, placement, motion, layout customization |
| D09 | Widgets | Enable/disable/reorder; disabled widgets cease optional background work |
| D10 | Performance | Balanced, adaptive motion, event-driven where possible |
| D11 | Backup | User-triggered import/export; no automatic cloud sync |
| D12 | Failed update | Restore previous working binary/version; protect user data |
| D13 | Extensions | Internal modular interfaces; no loading third-party code in v1 |
| D14 | Diagnostics | Local logs; only user-reviewed bug submission to GitHub |
| D15 | Release scope | Stable core widgets first |

**Additional fixed agreements:** system tray + standalone settings from v1; daily GitHub update checks; **ask before download and before install**; show version, size, features, fixes, status/progress; no silent telemetry; no mandatory elevation; no auto-enabling activity tracking; minimize data leakage on enterprise devices; credits/license notices in installer, About, and `THIRD_PARTY_NOTICES.md`.

### 0.1 Explicit v1 feature list
- Island shell: collapsed pill, peek, expanded, pinned; clock/date and responsive animations.
- Widgets: media transport/metadata, CPU/RAM/battery/uptime, master volume/mute, local notes, local task list.
- System tray: show/hide, pin, pause/resume, select/restore monitor, open settings, check updates, view logs/diagnostics, Safe Mode, restart, Exit.
- Dedicated settings: General, Appearance, Behavior, Widgets, Updates, Privacy/Data, Diagnostics, About/Credits.
- User startup consent, persistent preferences, DPI-awareness, monitor fallback, fullscreen-hide, single-instance guard, graceful shutdown, crash tracking, offline behavior, export/import, update notifications, verified installer/update/rollback workflow.
- Accessibility: keyboard navigation, focus/escape semantics, meaningful automation labels, contrast, reduced motion.

**Not v1:** live weather, Google/Outlook calendar, Wi-Fi connection manager, Bluetooth manager, screen brightness, file shelf, granular app-activity dashboard, external plugins, cloud sync, public accounts. Scaffold adapter interfaces only if needed; do **not** implement deferred features without updating requirements and progress.

### 0.2 Out-of-scope / prohibited shortcuts
No Electron, Chromium/WebView dependency, kernel driver, Windows service, elevation-by-default, antivirus/enterprise-control bypass, invasive hooks/injection, screenshot capture or keystroke monitoring, secret tokens inside releases, forced system-tray visibility, silent diagnostics uploads, unbounded API polling, or unconditional `DispatcherUnhandledException.Handled = true`.

## 1. Ground rules for any implementation agent

1. First read `AGENTS.md` then `docs/AGENT_PROGRESS.md`; inspect repository for actual state; **never assume listed work is complete without evidence**.
2. Work on **one task ID at a time** in build order. Before code, give a 3–6-line plan and expected test. Prefer smallest vertical change.
3. Use `dotnet build`, tests, linters and Windows manual checks as applicable. Linux agents may generate or review code, but **cannot certify WPF GUI/Win32 behavior** without a Windows runner. Mark `BLOCKED_WINDOWS_VALIDATION`, not `DONE`.
4. After each meaningful unit: update `docs/AGENT_PROGRESS.md` **in the same commit**; log changed files, exact commands and outcomes, next task, blockers, decision changes. Keep the `NEXT ACTION` section precise enough for a fresh model context.
5. Minimize tokens: read relevant task section and affected code only; reference this file by task ID rather than summarizing it in full; avoid repeatedly listing repository files or re-deriving settled decisions. See `AGENTS.md`.
6. Do not create placeholder code labelled complete. Errors and test skips are not success. Record limitations honestly.
7. Ask the owner only when a new decision materially changes cost, privacy, security, OS support or locked scope; otherwise follow defaults and add an ADR.

## 2. Technology and dependency decisions

| Area | Default | Constraints |
|---|---|---|
| Runtime | .NET 10 LTS WPF, C# | Pin SDK with `global.json`; verify runtime/publish on supported Windows target |
| View architecture | MVVM with commands, DI via Generic Host | Keep business logic out of code-behind; code-behind limited to platform interactions |
| Rendering | WPF visuals, native Win32 for borderless/topmost/hit-testing | Correct keyboard focus and accessibility; no intercepting clicks outside pill |
| Platform | `P/Invoke` and Windows Runtime adapters | No admin-only APIs as required core paths |
| Data | SQLite for notes/tasks; atomic JSON for settings | Define schema version/migrations/backups before first migration |
| Logging | `Microsoft.Extensions.Logging` + local rolling file sink (evaluate license) | Retention 14 days and size cap; scrub secrets/PII |
| Tray | `NotifyIcon` or Win32 `Shell_NotifyIcon` | Explorer restart re-registration, DPI icons, disposal |
| Media | `GlobalSystemMediaTransportControlsSessionManager` | Player-dependent capabilities; degrade gracefully |
| Tests | xUnit/NUnit, UI smoke tests on Windows, CI | Pin dependency versions; coverage on critical services |
| Installer | Evaluate **WiX per-user** vs **Inno Setup per-user** | Record license/security/enterprise validation; no HKLM writes |
| Updates | GitHub public releases; signed/hash-verified artifacts; separate updater | Explicit approvals; stable channel; no elevation or in-use replacement |
| Icons | Licensed open-source icon set | Include actual notices and credits |

**Build/package caveat:** Single-file .NET publishing is an output packaging option, not evidence that *all* dependent files can be safely updated in-place. For production, distinguish (a) portable user-facing `DynamicIsland.exe` launcher and (b) immutable versioned app payload; a single self-contained EXE alone does not guarantee reliable rollback. If implementing a true movable single-file portable EXE, use a separate update helper, backed-up original, atomic swap/restart, and clear fallback on unwritable directories. Validate antivirus/SmartScreen and extraction behavior.

**No free-signing promise:** A zero-cost build can be unsigned, but enterprise allow-listing and SmartScreen reputation are outside our control. Never describe checksums alone as proof of publisher authenticity. Support hashes and optional trusted signature verification; verify update metadata provenance before unattended extraction or execution. If trust cannot be established, ask user to visit verified GitHub Release manually and disable one-click installation.

## 3. Repository / solution architecture

```text
DynamicIsland/
├── AGENTS.md                         # Agent instructions — required
├── docs/
│   ├── AGENT_PROGRESS.md             # Session handoff / evidence — required
│   ├── IMPLEMENTATION_GUIDE.md       # This document in repository
│   ├── ADR/                            # Decision records
│   ├── DESIGN_SYSTEM.md
│   ├── SECURITY_PRIVACY.md
│   ├── TEST_MATRIX.md
│   └── RELEASE_PROCESS.md
├── src/
│   ├── DynamicIsland.App/            # WPF host, shell, settings, tray, view models
│   ├── DynamicIsland.Core/           # Domain, interfaces, widget state/lifecycle
│   ├── DynamicIsland.Platform.Windows/ # OS adapters, monitor, audio, media, session
│   ├── DynamicIsland.Widgets/        # Built-in compiled clock, media, stats, notes, tasks
│   ├── DynamicIsland.Infrastructure/ # Settings, SQLite, logs, HTTP/update metadata
│   ├── DynamicIsland.Launcher/       # Optional stable path / crash supervision
│   └── DynamicIsland.Updater/        # Separate short-lived updater
├── tests/
│   ├── DynamicIsland.UnitTests/
│   ├── DynamicIsland.IntegrationTests/
│   └── DynamicIsland.WindowsSmokeTests/
├── packaging/
│   ├── portable/
│   └── installer/
├── .github/workflows/
├── THIRD_PARTY_NOTICES.md
├── LICENSE
├── README.md
├── global.json
└── DynamicIsland.sln
```

**Dependency direction:** `Core` knows no WPF or Windows APIs. `Widgets` depends on `Core` interfaces. `Platform.Windows` and `Infrastructure` implement core abstractions. `App` composes dependencies. `Updater` operates out of process; only calls stable update interfaces/protocol. `Launcher` must never become a long-running background service without a demonstrated need.

**Core contracts:** `IWidget`, `IWidgetLifecycle`, `IIslandStateMachine`, `ISettingsStore`, `IMediaSessionService`, `ISystemMetricsService`, `IAudioService`, `IMonitorService`, `IFullscreenDetector`, `IStartupRegistration`, `IUpdateChecker`, `IUpdateInstaller`, `IDataBackupService`, `ICrashStateStore`, `INotificationService`, `IClock`. All external resources support cancellation and disposal. No external plugin reflection/loading in v1.

## 4. State, lifecycle, persistence and security specifications

### 4.1 Island state machine
States: `Hidden`, `Collapsed`, `Peek`, `Expanded`, `Pinned`, `SuspendedFullscreen`, `Paused`. Transitions are deterministic and unit-tested. Click from collapsed -> expanded; hover -> peek after debounce; pointer exit -> collapsed if not pinned; Escape -> collapsed; user pin stays expanded except fullscreen/privacy override; fullscreen auto-hide restores previous visible state; Pause freezes optional providers and hides island while tray remains; shutdown disposes everything.

**Focus/click rules:** Always-on-top `WS_EX_TOOLWINDOW`-style behavior only where appropriate; WPF no-activate handling must not block interactive widgets. Hit testing limited to visible geometry. Restore focus to previous foreground window where possible without force. Don't promise overlay above exclusive fullscreen games or secure desktops. Handle taskbar/notification focus, remote desktop, rapid monitor changes, DPI at 100/125/150/200%.

### 4.2 Widget lifecycle
`Created -> Initialized -> Active -> Inactive -> Disabled/Disposed`; initialize independent providers asynchronously after shell appears. Each widget owns bounded work; failures render widget-level error; retries use timeout and exponential backoff+jitter; no UI-thread blocking; disable stops polling and subscriptions. `CancellationToken` throughout. Observe exceptions in fire-and-forget tasks (prefer no fire-and-forget).

### 4.3 Paths and persistence
- Installed version: binaries under `%LOCALAPPDATA%\Programs\DynamicIsland\` or equivalent per-user install path.
- Portable: app can run from writable/nonwritable folders; data still defaults to `%LOCALAPPDATA%\DynamicIsland\`, never next to EXE unless a separate explicit portable-data mode is later approved.
- Settings: `%LOCALAPPDATA%\DynamicIsland\data\settings.json` with `schemaVersion`, atomic write-to-temp and replace, validation and backup on corrupt load.
- SQLite: `%LOCALAPPDATA%\DynamicIsland\data\island.db`; parametric SQL; migrations with transactions and verified data backup/restore; never blindly downgrade an incompatible DB.
- Logs: `%LOCALAPPDATA%\DynamicIsland\logs\`, capped and rotated; optional crash report JSON sanitized.
- Backup: user-selected archive with manifest, schema and checksums; exclude access tokens by default; preview/validate import, schema compatibility, rollback on import failure.

### 4.4 Startup and single instance
First-run wizard asks permission to create **HKCU Run** registration or current-user Startup shortcut. Default sign-in behavior: start collapsed after ~5-second app-side delay (OS actual timing not guaranteed). No service, scheduled task requiring elevation, or HKLM. Maintain stable startup launcher path across updates; repair stale registration when moving portable EXE; clear on uninstall. Use per-user named mutex + secure local IPC for second-instance activation (do not rely on mutex alone to bring existing window forward); ensure user session isolation. Respond to logoff/shutdown promptly.

### 4.5 Error classes
- Expected transient: offline/API timeout/no media session -> unobtrusive widget state; retry w/ cap.
- Recoverable integration: adapter disconnect -> isolate/reinit; nonblocking UI.
- Fatal: log best-effort, mark session unhealthy and exit; do not pretend unhandled exceptions are recoverable.
- Startup crash: child launcher tracks crash timestamps, at most 2 automatic restarts in rolling 5 minutes; next restart enters Safe Mode or stops to prevent loops. Explicit Exit suppresses relaunch. Clean shutdown clears unhealthy flag. Critical failure in launcher/updater must stop and surface manual recovery instructions.
- Runtime state: crash reports must not include note text, file names, window titles, tokens, secrets or clipboard data by default.

### 4.6 Security/privacy threat model
Trust boundaries: GitHub update metadata/assets; imported backup files; OAuth providers (post-v1); local settings; system API responses; user-created note/task text; context menus; process paths. Validate URL allow-list (`https` GitHub-approved endpoints), archive traversal/symlinks, manifest schemas, file hashes, launch arguments, install path, and size limits. Never execute remotely delivered scripts or untrusted plugin assemblies. Locally redact and rate-limit logs. OAuth later requires PKCE and secure OS credential storage, never client secrets in distributed binaries. Prompt before any data/network operation other than explicitly chosen update checking. In managed deployments, allow self-update disable policy/setting.

## 5. Sequential implementation work breakdown (do not skip gates)

All task IDs have explicit evidence requirements. **Mark `DONE` only after tests and the relevant Windows manual checklist pass; otherwise mark `IMPLEMENTED_AWAITING_VALIDATION`**. If blocked, capture the exact next command/verification step in progress file.

### Phase P00 — Baseline and environment (gate G00)
- **P00-01** Initialize Git repo and docs; copy guide/AGENTS/progress; create LICENSE (MIT for original code contingent on third-party licenses), README, CONTRIBUTING, third-party notices template.
- **P00-02** Verify Windows test host and SDK; pin supported .NET 10 SDK in `global.json`; record installed versions and OS/architecture. Confirm supported Windows 10 SKUs/builds explicitly; no generic Windows 10 security-support guarantee.
- **P00-03** Create solution/projects/dependency boundaries, DI host, config and test projects; enforce nullable, analyzers, warnings baseline; set deterministic build/lockfile policy.
- **P00-04** Configure GitHub Actions Windows runner for restore/build/unit tests; pull-request checks; no unsigned artifacts described as trusted.
- **G00 PASS:** fresh clone restores/builds/tests on Windows runner; progress and docs exist.

### Phase P01 — Reliability kernel first (gate G01)
- **P01-01** Typed settings schema/version, defaults, validation, corruption fallback and atomic writes; migration tests.
- **P01-02** Rolling structured local logs; retention/size cap and privacy redaction; logger initialized before UI.
- **P01-03** App lifecycle coordinator: explicit shutdown, session-ending/Exit distinct from unexpected crashes; cancellation/disposal; single-instance mutex + activation IPC.
- **P01-04** Safe Mode state and rolling crash counter; two retries per five minutes; launcher/spawn strategy; mock-process tests, injected crash tests.
- **P01-05** Windows integration error boundaries; nonfatal error display contracts and diagnostic export preview.
- **G01 PASS:** corrupt settings recover; simulated widget failure isolated; deliberate Exit never auto-relaunches; third crash does not loop; no duplicate app.

### Phase P02 — Floating shell and rendering (gate G02)
- **P02-01** Implement island window and monitor placement service; top-center default, configurable offsets, work area, display ID persistence and primary fallback.
- **P02-02** Per-monitor DPI awareness, display change reactions, resolution/taskbar changes, correct no-click-through geometry; high-DPI icons.
- **P02-03** Deterministic state machine, hover debounce/peek, click expand, collapse, pin, keyboard focus and Escape, transition animations; no invisible interaction blockers.
- **P02-04** Fullscreen detection with game-safe hide/restore; configurable exceptions; never assume exclusive-fullscreen overlay will work.
- **P02-05** Adaptive frame/activity budget, reduced motion, power-aware control, basic visual QA against screenshot reference with original artwork rather than copied proprietary assets.
- **G02 PASS:** 100/125/150/200% DPI; 1/2 monitors; unplug/rotate; fullscreen; sleep/wake; normal mouse and keyboard control; verify on actual Windows.

### Phase P03 — Tray, settings, sign-in (gate G03)
- **P03-01** Native tray icon with left-click toggle, right-click accessible menu; recover on Explorer restart; remove on Exit.
- **P03-02** Tray actions: show/hide, pin, pause/resume, move/reset display, settings, update check, logs, Safe Mode, restart, Exit; prevent accidental app shutdown when settings closes.
- **P03-03** Dedicated WPF settings navigation: General/Appearance/Behavior/Widgets/Updates/Privacy/Diagnostics/About; explicit Save/auto-save semantics, validation, reset.
- **P03-04** First-run onboarding asking sign-in startup permission, privacy, widget choices; HKCU-only start; stable launcher path and 5s default delay; disabled startup obeyed.
- **P03-05** Global shortcut optional/customizable; conflicts detected; shortcut works only if OS grants registration.
- **G03 PASS:** Explorer restart restores tray; sign-in starts one collapsed island when opted in; user can fully exit, change monitor, reset off-screen position.

### Phase P04 — Built-in widgets and data (gate G04)
- **P04-01** Widget registry/lifecycle: compiled-in modules, enable/disable/reorder; isolate failures; memory and background work cease on disable.
- **P04-02** Clock/date widget, time/locale formats, timers only if approved for v1; avoid 60fps ticking.
- **P04-03** Media widget via global Windows media sessions; detect none/multiple players, stale sessions, unsupported skip/play actions; cover album art loading safely.
- **P04-04** Metrics widget CPU/RAM/battery/uptime; choose efficient API; handle no battery, permissions, sleeping, counters reset; update cadence adaptive.
- **P04-05** Volume/mute widget; device default changes, missing output device, external changes; don't assume per-app volume control.
- **P04-06** SQLite notes and tasks: CRUD, edits, completion, timestamps, validation; transactional changes, search optional; no network.
- **P04-07** Backup/export/import with schemas, checksums, dry-run preview, atomic restore; corrupt archive and version mismatch tests; user-selected destination.
- **G04 PASS:** all core widgets work without internet, survive 100 restarts and update-shaped migrations in tests; one failing widget never crashes others.

### Phase P05 — Customization, UX, accessibility, performance (gate G05)
- **P05-01** Theme tokens/colors, radius/size/opacity/placement, visual hierarchy; enforce min/max safe dimensions and visibility.
- **P05-02** Persist reorder/pin/preferences; widget configuration errors recover; update UI without full shell recreation.
- **P05-03** Keyboard-only flow, focus indicators, AutomationProperties, screen reader check, high contrast, reduced motion, minimum touch targets.
- **P05-04** Idle CPU/memory and battery scenarios; caching, adaptive timers, suspension when fullscreen/paused/disabled; log benchmark setup/results.
- **G05 PASS:** target idle CPU <=0.5% and private memory <=100MB on reference machine **as goals, not guarantees**; document deviations; no runaway timers or obvious UI leaks.

### Phase P06 — Distribution and update integrity (gate G06)
- **P06-01** Select install and portable layout via proof of concept; immutable versioned payload + stable launch path where practical; document executable move, read-only folder, antivirus and company-policy failure modes.
- **P06-02** Per-user installer with no elevation, Start menu, uninstall cleanup without deleting user data unless explicitly requested; IT-managed flag disables self-update. Test Standard User account.
- **P06-03** Release metadata: compare SemVer (including prerelease policies) to installed version, stable channel, daily checks with local cache/jitter/backoff; API `releases/latest` or static GitHub Pages manifest; user agent and quota awareness.
- **P06-04** Update UI: version, release date, download bytes formatted, notes sections (features/fixes), link, progress/cancel, retry. **Separate download approval and install approval**. Never silently install.
- **P06-05** Download to temp staging, verify size/checksum AND trustworthy publisher/source provenance (pinned signature/public key when available), validate extracted files and version. If provenance unavailable, direct user to manual release and don't auto-execute. Never trust unreviewed release-body HTML as executable content.
- **P06-06** Separate updater protocol: prepare, exit gracefully, wait for process, swap to new version, launch, health handshake within bounded time, commit; failure triggers rollback. Leave old version intact until health confirmed. Never overwrite in-use executable.
- **P06-07** Data migration safety: pre-update snapshot, schema compatibility and transaction, recover gracefully from failed migrations, never blindly rollback binary against incompatible DB; selective restore only after explicit validation.
- **P06-08** Portable update fallback for unwritable folders; IT-managed/manual mode; cancel download and disk-space failures; certificate/signing path optional and no claims of guaranteed SmartScreen bypass.
- **P06-09** CI GitHub Release pipeline: pinned dependencies, build/test/package x64, hashes, changelog/release notes, optional provenance attestation/signing, publish stable/beta tags to appropriate channel; generate/update manifest only after successful publish.
- **G06 PASS:** tests for no update, valid update, user declines, network loss, bad checksum/signature, corrupt archive, not enough space, locked file, crash before health acknowledgment, failed DB migration, rollback and clean restart; test both portable and per-user standard-user install on Windows.

### Phase P07 — Security, compatibility and launch (gate G07)
- **P07-01** Threat-model review; dependency/license check, secrets audit, archive path traversal test, input validation, unexpected permission denials.
- **P07-02** Windows matrix: Windows 11 supported x64; Windows 10 compatible Enterprise/LTSC and selected consumer builds clearly labelled best-effort/out-of-support; high DPI, 1/2 monitors, laptop/dock, remote desktop, battery/no-battery, audio/no-audio, network/offline, standard account, enterprise policy blocks.
- **P07-03** Install/uninstall and first-run UX; portable moved/deleted, fixed startup entry repair, app update rollback from real signed or deliberately manual release; log privacy and no telemetric endpoints test.
- **P07-04** README screenshots, feature matrix, supported OS matrix, licensing, install guides for portable and per-user, troubleshooting, bug-report redaction, change log, acknowledgements, security policy and contribution guide.
- **P07-05** Tag `v1.0.0` only after every gate has evidence; publish assets and release notes; monitor user-reported defects manually (no automatic tracking).
- **G07 PASS:** all P00–P07 evidence linked in progress file and release checklist; no P0/P1 defects or unverified critical behavior mislabelled as working.

### Post-v1 backlog (never block v1 release)
`B01` Weather with provider terms/attribution/caching and no paid API; `B02` Google/Outlook calendar OAuth/PCKE/provider policy; `B03` Wi-Fi scan/connect; `B04` Bluetooth discover/pair capability-specific; `B05` brightness per display/hardware; `B06` drag-and-drop file shelf; `B07` opt-in local activity analytics; `B08` safe plugin model/security design; `B09` ARM64; `B10` localization. Evaluate each provider's terms again before implementation. Public/free is **not** automatically licensed for every use; attribution alone does not grant rights.

## 6. Definition of Done & evidence specification

Each task is `DONE` only if (1) behavior implemented, (2) appropriate automated tests added/passing, (3) Windows manual verification where relevant, (4) docs updated, (5) no unexpected permission elevation/privacy regression, and (6) progress file updated. For Windows UI not testable in current environment, use `IMPLEMENTED_AWAITING_VALIDATION` and explicitly defer gate. `NOT_STARTED`, `IN_PROGRESS`, `BLOCKED`, `IMPLEMENTED_AWAITING_VALIDATION`, `DONE` are the only status values.

**Test categories:** unit / integration / platform integration / GUI smoke / install update rollback / performance / security / accessibility. Always list exact command, host OS and result (pass/fail/not run). Tests are evidence, not claims.

**Initial manual Windows acceptance checklist:** startup HKCU opt-in/off; tray after Explorer restart; monitor disconnect; 125/150/200% DPI; fullscreen games and presentation; media sessions from multiple apps; no output audio; battery/no battery; offline; repeated crash; DB import rollback; 2 update consent steps; read-only portable folder; standard-user install/uninstall; clean application Exit and relaunch; no duplicate instances; telemetry absent.

## 7. Release workflow, versioning and update behavior

- Use SemVer `MAJOR.MINOR.PATCH`, stable tags `vX.Y.Z`, optional beta `vX.Y.Z-beta.N` visible only if user chooses beta channel later.
- GitHub release assets: named architecture/package (`DynamicIsland-Portable-win-x64.zip`, `DynamicIsland-Setup-win-x64.exe`), checksums, manifest, release notes. Asset sizes shown from GitHub metadata/manifest; never invent update size.
- Update check after startup as allowed by settings if last check >=24h (with jitter), graceful failure offline; manual Check Now bypasses normal interval but rate limits.
- User interaction: notification -> release details -> **Download** approval -> download/verify -> **Install** approval -> postpone or install -> health check -> commit/rollback -> clear changelog on success.
- Enterprise managed `DisableSelfUpdate`: show details and IT support route, no self-install; do not assume corporate policy can be bypassed.
- Release pipeline must publish checksum/attestation and treat CI permissions/third-party actions as supply-chain attack surfaces; pin action versions and use least-privilege tokens.

## 8. Agent continuity/handoff procedure

At the **start of every session**:
1. Read `AGENTS.md` (<2 pages) and first 100 lines of `docs/AGENT_PROGRESS.md`.
2. Inspect `git status`, current branch, latest commit, and listed files only. Reconcile progress claims with actual repo/tests. Do not re-read entire guide unless current task requires it.
3. Open the **single current task ID** section above and relevant source/tests; summarize what exists, what remains, and the next verification.
4. Execute task, run tests, record evidence, update progress, commit. If interrupted, leave an exact `NEXT ACTION` with command and file paths.

At the **end of every session**:
1. Update one-line overall state, active task ID/status and last successful gate.
2. Write concise `RECENT VERIFIED CHANGES` (max 10 entries), `BLOCKERS`, `NEXT ACTION` (ONE task with steps and verification commands), `DECISIONS/ADRs`.
3. Mark Windows-only checks honestly. Do not erase failures or unresolved questions.
4. State a compact user-facing handoff: completed / tested / next / blockers. Avoid repeating this entire specification.

**Agent token budget discipline:** Don't reprint long documents. Prefer `git diff --stat`, targeted `rg`, specific sections. Read build logs only at failure locations. No broad speculative refactors. Avoid new dependencies without justification. If context space is low, update progress **before** ending. A new agent must be able to proceed solely from repo + `AGENTS.md` + `docs/AGENT_PROGRESS.md` + current task section.

## 9. ADR trigger list (document before changing)

Create `docs/ADR/NNNN-title.md` with context/decision/alternatives/consequences whenever changing framework/runtime, OS support, updater trust model, install format, privacy defaults, storage schema, licenses, plugin policy, project budget or user-approved release behavior. Never silently override D01–D15.

## 10. Authoritative external references (verify at time of implementation)

- .NET support policy: https://dotnet.microsoft.com/en-us/platform/support/policy
- .NET on Windows support matrix: https://learn.microsoft.com/en-us/dotnet/core/install/windows
- WPF documentation: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/
- Windows `Run` registry keys: https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys
- Windows notification area: https://learn.microsoft.com/en-us/windows/win32/shell/notification-area
- GitHub Releases API: https://docs.github.com/en/rest/releases/releases
- GitHub API rate limits: https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api
- GitHub Actions security: https://docs.github.com/en/actions/security-guides/security-hardening-for-github-actions
- Windows security/app controls: https://learn.microsoft.com/en-us/windows/security/application-security/application-control/

**First executable instruction for the next AI agent:** Read `AGENTS.md`, then `docs/AGENT_PROGRESS.md`. Start `P00-01`. Do **not** jump into widget implementation or updater code before foundational gates.
