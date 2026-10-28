# Changelog

All notable changes to Dynamic Island are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/).

The release workflow copies the section whose heading matches the tag (for example `## [1.0.0] - 2026-11-30`) into the
GitHub release notes, so each version needs its own section before it is tagged. Notes are shown in the app as plain
text: use headings and bullet points, not HTML.

## [Unreleased]

### Added

- The island at the top of the screen, shaped like a notch, with compact and expanded states, DPI and multi-monitor
  placement, fullscreen and click-through handling.
- Tray icon and menu, settings window, keyboard shortcut, and an opt-in Windows startup entry.
- Core widgets: media, clock, calendar, notes, tasks, timer, system status and others, with local notes and tasks
  storage and backup/restore.
- Themes, layout options, and performance budgets.
- Portable ZIP and per-user installer, with no administrator rights needed.
- Daily update checks (opt-in) with separate Download and Install approvals, signed release manifests, a separate
  updater with health check and automatic rollback, and data snapshots around updates.
