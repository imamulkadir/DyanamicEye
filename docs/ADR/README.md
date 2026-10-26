# Architecture Decision Records

Record any change to a frozen decision (guide §0, D01–D15) or any item in the guide's §9 ADR trigger list here
**before** implementing it.

- File name: `NNNN-short-title.md` (zero-padded, sequential).
- Status: `Proposed` → `Accepted` / `Rejected` / `Superseded by NNNN`.
- Reference the ADR from `docs/AGENT_PROGRESS.md` (Architecture Decisions section).

## Template

```markdown
# NNNN — Title

- Status: Proposed
- Date: YYYY-MM-DD
- Task: Pxx-yy

## Context
What forces the decision; constraints from the guide.

## Decision
What we will do.

## Alternatives considered
Option — why rejected.

## Consequences
Security, privacy, OS support, cost and maintenance impact; follow-up tasks.
```

## Index
- [0001 — Supported Windows versions for v1](0001-supported-windows-versions.md) — Accepted (P00-02)
- [0002 — Solution layout, build policy and test platform](0002-solution-layout-and-test-platform.md) — Accepted (P00-03)
- [0003 — Settings persistence, recovery and versioning](0003-settings-persistence.md) — Accepted (P01-01)
- [0004 — Local logging sink and redaction](0004-local-logging.md) — Accepted (P01-02)
- [0005 — App lifecycle, exit classification and single instance](0005-lifecycle-and-single-instance.md) — Accepted (P01-03)
- [0006 — Crash supervision, Safe Mode and test-only hooks](0006-crash-supervision-and-safe-mode.md) — Accepted (P01-04)
- [0007 — Component error boundaries, user notices and diagnostic export](0007-error-boundaries-notices-diagnostics.md) — Accepted (P01-05)
- [0008 — Island window, monitor identity and placement](0008-island-window-and-placement.md) — Accepted (P02-01, amended P02-02)
- [0009 — Island state machine, interaction and accessibility](0009-island-states-and-interaction.md) — Accepted (P02-03, amended: notch design)
- [0010 — Fullscreen detection and auto-hide](0010-fullscreen-detection.md) — Accepted (P02-04, amended: cloaked windows)
- [0011 — Activity budget, power-aware motion and visual regression](0011-activity-budget-motion-visual-qa.md) — Accepted (P02-05)
- [0012 — Notification-area (tray) icon](0012-tray-icon.md) — Accepted (P03-01)
- [0013 — Tray actions, settings service, restart contract and shutdown mode](0013-tray-actions-settings-restart.md) — Accepted (P03-02)
- [0014 — Settings window: navigation, save semantics, validation and reset](0014-settings-window.md) — Accepted (P03-03)
- [0015 — First-run onboarding and sign-in startup](0015-first-run-and-sign-in-startup.md) — Accepted (P03-04)
- [0016 — Notch clock](0016-notch-clock.md) — Accepted (owner request; collapsed part of P04-02)
- [0017 — Optional global shortcut](0017-global-shortcut.md) — Accepted (P03-05)
- [0018 — Widget registry and lifecycle](0018-widget-registry.md) — Accepted (P04-01)

Pending: per-user installer technology (P06), updater trust/provenance model (P06).
