using DynamicIsland.Core.Distribution;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.Core.Updates;

/// <summary>
/// Decides when to ask GitHub for the latest release and what the answer means (guide P06-03, §7, ADR 0032):
/// <list type="bullet">
/// <item>IT-managed copies never check (ADR 0031); automatic checks need the user's consent; "Check now" does not.</item>
/// <item>Automatic: at most once per <see cref="Interval"/> plus a per-install jitter; after failures, backs off from
/// <see cref="FirstRetry"/> doubling up to the interval.</item>
/// <item>GitHub's rate-limit reset is respected for every check; "Check now" again within <see cref="ManualCooldown"/>
/// returns the last answer instead of asking again.</item>
/// <item>Only a newer stable release counts as an update; the answer is cached, with its ETag, between runs.</item>
/// </list>
/// Thread-safe; one check runs at a time.
/// </summary>
public sealed class UpdateChecker(
    IReleaseSource source,
    IUpdateStateStore store,
    Func<AppSettings> settings,
    UpdatePolicy policy,
    SemanticVersion installed,
    TimeProvider time,
    TimeSpan jitter) : IUpdateChecker, IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    public static readonly TimeSpan MaxJitter = TimeSpan.FromHours(2);
    public static readonly TimeSpan FirstRetry = TimeSpan.FromHours(1);
    public static readonly TimeSpan ManualCooldown = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan DefaultRateLimitWait = TimeSpan.FromHours(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private UpdateCheckState? _state;

    public UpdateCheckResult? Last { get; private set; }

    /// <summary>When the next automatic check is due (for the scheduler); <c>null</c> when it is due now.</summary>
    public DateTimeOffset? NextAutomaticCheckUtc
    {
        get
        {
            var state = _state ??= store.Load();
            var next = NextDue(state);
            return next is { } due && due > time.GetUtcNow() ? due : null;
        }
    }

    public async Task<UpdateCheckResult> CheckAsync(bool manual, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = _state ??= store.Load();
            var now = time.GetUtcNow();
            UpdateCheckResult result;

            if (policy.SelfUpdateDisabled)
            {
                result = new UpdateCheckResult(UpdateCheckOutcome.DisabledByPolicy);
            }
            else if (!manual && settings().Updates.AutomaticChecks != ConsentState.Granted)
            {
                result = new UpdateCheckResult(UpdateCheckOutcome.NotAllowed);
            }
            else if (state.RetryAfterUtc is { } retryAfter && retryAfter > now)
            {
                result = Compose(state) with { Outcome = UpdateCheckOutcome.RateLimited };
            }
            else if (manual && state.LastAttemptUtc is { } attempt && now - attempt < ManualCooldown && state.ConsecutiveFailures == 0)
            {
                result = Compose(state); // Asked moments ago: the same answer, without another request.
            }
            else if (!manual && NextDue(state) is { } due && due > now)
            {
                result = Compose(state) with { Outcome = UpdateCheckOutcome.NotDue };
            }
            else
            {
                var fetch = await source.FetchLatestAsync(state.ETag, cancellationToken).ConfigureAwait(false);
                (state, result) = Apply(state, fetch, now);
                _state = state;
                store.Save(state);
            }

            Last = result;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The available update to announce in the tray (ADR 0033), once per version: returns it and remembers it, or
    /// <c>null</c> when there is none or it was already announced.
    /// </summary>
    public ReleaseInfo? TakeNotice()
    {
        _gate.Wait();
        try
        {
            var state = _state ??= store.Load();
            if (UpdateMessages.Newer(Last, installed) is not { } latest
                || string.Equals(state.NotifiedVersion, latest.Version.ToString(), StringComparison.Ordinal))
            {
                return null;
            }

            _state = state with { NotifiedVersion = latest.Version.ToString() };
            store.Save(_state);
            return latest;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private (UpdateCheckState State, UpdateCheckResult Result) Apply(UpdateCheckState state, ReleaseFetch fetch, DateTimeOffset now)
    {
        var attempted = state with { LastAttemptUtc = now, RetryAfterUtc = null };
        switch (fetch.Status)
        {
            case ReleaseFetchStatus.Found when fetch.Release is not null:
                var found = attempted with { LastSuccessUtc = now, ConsecutiveFailures = 0, ETag = fetch.ETag, Latest = fetch.Release, NoReleases = false };
                return (found, Compose(found));

            case ReleaseFetchStatus.NotModified when state.Latest is not null || state.NoReleases:
                var same = attempted with { LastSuccessUtc = now, ConsecutiveFailures = 0 };
                return (same, Compose(same));

            case ReleaseFetchStatus.NoRelease:
                var none = attempted with { LastSuccessUtc = now, ConsecutiveFailures = 0, ETag = null, Latest = null, NoReleases = true };
                return (none, Compose(none));

            case ReleaseFetchStatus.RateLimited:
                var limited = attempted with { ConsecutiveFailures = state.ConsecutiveFailures + 1, RetryAfterUtc = fetch.RetryAfterUtc ?? now + DefaultRateLimitWait };
                return (limited, Compose(limited) with { Outcome = UpdateCheckOutcome.RateLimited });

            default:
                // Unreachable, invalid, or a 304 we cannot use: try again later with a full request.
                var failed = attempted with { ConsecutiveFailures = state.ConsecutiveFailures + 1, ETag = fetch.Status == ReleaseFetchStatus.Unreachable ? state.ETag : null };
                return (failed, Compose(failed) with { Outcome = UpdateCheckOutcome.Failed });
        }
    }

    private UpdateCheckResult Compose(UpdateCheckState state)
    {
        var outcome = state.NoReleases ? UpdateCheckOutcome.NoReleases
            : state.Latest is { } latest && latest.Version > installed ? UpdateCheckOutcome.UpdateAvailable
            : UpdateCheckOutcome.UpToDate;
        return new UpdateCheckResult(outcome, state.Latest, state.LastSuccessUtc);
    }

    /// <returns>When the next automatic check is due, or <c>null</c> for "now" (never checked).</returns>
    private DateTimeOffset? NextDue(UpdateCheckState state)
    {
        if (state.RetryAfterUtc is { } retryAfter && retryAfter > time.GetUtcNow())
        {
            return retryAfter;
        }

        if (state.ConsecutiveFailures > 0 && state.LastAttemptUtc is { } attempt)
        {
            var backoff = TimeSpan.FromTicks(Math.Min(Interval.Ticks, FirstRetry.Ticks << Math.Min(state.ConsecutiveFailures - 1, 10)));
            return attempt + backoff;
        }

        return state.LastSuccessUtc is { } success ? success + Interval + Clamp(jitter) : null;
    }

    private static TimeSpan Clamp(TimeSpan value) =>
        value < TimeSpan.Zero ? TimeSpan.Zero : value > MaxJitter ? MaxJitter : value;
}
