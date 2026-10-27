using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Updates;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.App;

/// <summary>
/// Runs the automatic update check (ADR 0032): a first look <see cref="StartupDelay"/> after start (never slowing the
/// start itself), then whenever <see cref="UpdateChecker"/> says one is due, re-evaluating at least every
/// <see cref="Recheck"/> so a changed consent or a resumed PC is noticed. Consent, IT policy, intervals and rate limits
/// are all decided by the checker; evaluating them costs no network. Not started in Safe Mode.
/// </summary>
public sealed class UpdateCheckScheduler(UpdateChecker checker, LifecycleCoordinator lifecycle, TimeProvider time, ILogger<UpdateCheckScheduler> logger)
{
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan Recheck = TimeSpan.FromHours(1);
    private static readonly TimeSpan MinimumWait = TimeSpan.FromMinutes(1);

    /// <summary>The background loop (tests).</summary>
    public Task Running { get; private set; } = Task.CompletedTask;

    public void Start() => Running = RunAsync(lifecycle.ApplicationStopping);

    private async Task RunAsync(CancellationToken stopping)
    {
        try
        {
            await Task.Delay(StartupDelay, time, stopping).ConfigureAwait(false);
            while (!stopping.IsCancellationRequested)
            {
                var result = await checker.CheckAsync(manual: false, stopping).ConfigureAwait(false);
                if (result.Outcome is not (UpdateCheckOutcome.NotDue or UpdateCheckOutcome.NotAllowed or UpdateCheckOutcome.DisabledByPolicy)
                    && logger.IsEnabled(LogLevel.Information))
                {
                    var latest = result.Latest?.Version.ToString();
                    AppLog.UpdateCheckFinished(logger, result.Outcome, latest);
                }

                var wait = checker.NextAutomaticCheckUtc is { } next ? next - time.GetUtcNow() : Recheck;
                wait = wait < MinimumWait ? MinimumWait : wait > Recheck ? Recheck : wait;
                await Task.Delay(wait, time, stopping).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}
