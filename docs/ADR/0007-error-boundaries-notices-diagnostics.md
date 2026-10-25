# 0007 — Component error boundaries, user notices and diagnostic export

- Status: Accepted
- Date: 2026-10-25
- Task: P01-05

## Context
- Guide §4.2: widgets own bounded work and render failures at widget level. Retries use timeouts and exponential backoff with jitter, nothing blocks the UI thread, and there is no unobserved fire-and-forget work.
- Guide §4.5 defines the error classes (transient, recoverable, fatal).
- D14: logs stay local, and bugs are submitted only after the user reviews the report.
- G01: a simulated widget failure must stay isolated.

## Decision
1. **`IIsolatedComponent` + `ComponentBoundary` (Core).**
   - Each optional unit of work (later: widget data providers and OS adapter subscriptions) runs inside its own boundary, on an observed background task linked to `ApplicationStopping`.
   - **Classification** (`DefaultFailureClassifier`; components can be explicit with `ComponentFailureException(kind, reasonCode)`):
     - **Transient:** timeouts, I/O, HTTP, cancellation from other causes. Retried indefinitely with backoff (1 s doubling, ±20 % jitter, capped at 5 min). After 3 consecutive failures an Info notice is shown once.
     - **Recoverable:** everything else. Reinitialized up to 3 times. The 4th failure in a row marks it `Failed`, publishes a Warning notice with an `open-diagnostics` action, and stops it until a restart.
     - **Fatal:** OOM, stack exhaustion, access violation, or explicitly fatal. Escalates to `LifecycleCoordinator.RequestShutdown(Fatal)`, which leads to crash supervision (ADR 0006).
   - `ReportHealthy()` resets the failure count. `ReportDegraded(code)` shows a non-error degraded state, such as "no media session".
   - Status records carry a reason code and an exception **type**, never a message.
2. **`ComponentHost` (Core)** registers components, starts them after the window is shown, and **skips optional ones in Safe Mode** (`SkippedSafeMode`). It registers each stop with the lifecycle (2 s per step); a component that ignores cancellation is reported as `TimedOut` and cannot block exit.
3. **`INotificationService` + `InMemoryNotificationService` (Core).**
   - Notices hold only validated keys (`[a-z0-9.-]`, max 64): a message key, an optional subject and an optional action id. Free text, paths and exception text are rejected by construction; UI text comes from resources (P02/P03).
   - An identical notice is suppressed for 10 minutes.
   - At most 5 notices per minute, and at most 20 active ones.
4. **`DiagnosticBundleBuilder` (Infrastructure).**
   - `BuildPreview` produces in-memory files: `summary.txt` (version, OS, .NET, Safe Mode, settings outcome, recent crash times, component statuses) and the last 500 lines of the app and launcher logs. All of it goes through `LogRedactor` again, and each file is capped at 512 K characters.
   - **Settings values, note/task data and window titles are never included.**
   - `WriteZipAsync(approvedPreview, path)` writes exactly the approved files plus `manifest.json` (names, sizes, SHA-256) to a **new** file (`CreateNew`, never overwrites).
   - There is no upload path; the GitHub issue flow (P03/P07) asks the user to attach the reviewed file manually.

## Alternatives considered
- Catching everything at the dispatcher level: rejected. It would hide failures and contradict guide §0.2.
- Polly for retries: rejected. The retry policy is about 20 lines, so a dependency isn't justified.
- Reading the logs again at export time: rejected, because the user must get exactly what they reviewed.

## Consequences
- Every widget or provider in P04 is an `IIsolatedComponent` registered as optional, and its work must honor the cancellation token.
- The P03 Diagnostics page shows the preview and calls `WriteZipAsync` only after the user confirms.
- Notice message keys need resource strings once the UI exists.
