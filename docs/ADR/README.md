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

Pending: per-user installer technology (P06), updater trust/provenance model (P06).
