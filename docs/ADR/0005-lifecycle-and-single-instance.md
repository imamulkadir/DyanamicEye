# 0005 — App lifecycle, exit classification and single instance

- Status: Accepted
- Date: 2026-10-25
- Task: P01-03

## Context
Guide §4.4 requires a per-user named mutex plus secure local IPC for second-instance activation, user-session isolation, and prompt handling of logoff and shutdown. §4.5 requires that a deliberate Exit be distinguishable from a crash, that a clean shutdown clear the unhealthy flag, and that unhandled exceptions never be passed off as recoverable. §0.2 forbids unconditional `DispatcherUnhandledException.Handled = true`.

## Decision
1. **`LifecycleCoordinator` (Core)** is the single owner of shutdown.
   - The first `RequestShutdown(reason)` wins and cancels `ApplicationStopping`.
   - Components stop in reverse registration order. Each step gets 2 s and the whole shutdown gets 5 s. A step that hangs or throws is reported by type name only and never blocks exit.
   - `ExitReason` values map to exit codes: `UserExit` and `SessionEnding` → 0, `Fatal` → 1, `Restart` → 3. The launcher (P01-04) relies on these codes.
2. **Session marker:** `data/session.running` is written at start and deleted on any non-fatal exit. If it is still present at the next start, the previous session crashed, was killed or lost power. P01-04 builds the crash counter on top of it.
3. **Single instance (Platform.Windows):**
   - The mutex is `Local\DynamicIsland.{UserSid}.instance` (per session and per user). It is created without ownership, so there is no thread-affinity or abandoned-mutex handling, and it exists while the handle stays open.
   - Activation goes over the pipe `DynamicIsland.{UserSid}.activate`:
     - The server is created with `FirstPipeInstance` and a protected DACL that grants only the current user SID.
     - The client connects with `CurrentUserOnly`, so it refuses a pipe owned by anyone else; that blocks squatting and impersonation.
     - The protocol is one request/reply exchange. The client sends the line `activate\n` and waits for `ok\n` (accepted) or `no\n` (rejected), so a successful signal means the running instance really accepted it.
     - The server reads at most 64 ASCII bytes within 2 s, runs the activation callback before replying, then waits for the client to close, with a timeout and at most 64 KiB drained.
     - The first version was fire-and-forget (write then close). On the slower CI runner the request could be lost even though the sender reported success; CI run 40198912460 failed this way. The reply step fixes that.
   - The second instance calls `AllowSetForegroundWindow(ASFW_ANY)`, signals, and exits with code 0 without building the host or touching any files.
   - If the pipe cannot be created, a warning is logged and the app runs without activation.
4. **Exception policy:**
   - **UI thread:** the exception is logged as Critical, then `RequestShutdown(Fatal)`. `Handled` is true only when that request starts a controlled fatal exit (exit code 1, marker kept). A second failure during shutdown stays unhandled and terminates the process.
   - **Background thread** (`AppDomain.UnhandledException`): logged as Critical and the log providers are flushed; the process then terminates as .NET does by default.
   - **`TaskScheduler.UnobservedTaskException`:** logged as **Error**, not fatal. .NET does not terminate on these by default, and making them fatal would turn latent fire-and-forget bugs into crash loops. Code should not use fire-and-forget work (guide §4.2).
5. **Startup order:** single instance → host and logger → session marker → host start → settings load (outcome logged) → window → activation listener.

## Alternatives considered
- `Global\` mutex: rejected. It would collide across sessions, for example the same user over RDP, and `Local\` + SID gives per-session, per-user isolation.
- Activation through window messages (`FindWindow` / `PostMessage`): rejected, because it depends on window titles, can be spoofed, and needs a window before the host exists.
- Treating unobserved task exceptions as fatal: rejected, see 4.

## Consequences
- The UI-thread fatal path still needs an injected-crash test. It is planned with P01-04's crash tests.
- P03's tray "Exit" calls `RequestShutdown(UserExit)`, and Safe Mode restart uses `Restart`.
- The smoke tests assume no other Dynamic Island instance is running for the same user while they run.
