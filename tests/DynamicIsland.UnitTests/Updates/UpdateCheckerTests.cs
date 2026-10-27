using DynamicIsland.Core.Distribution;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Updates;
using DynamicIsland.UnitTests.TestSupport;

namespace DynamicIsland.UnitTests.Updates;

/// <summary>When to ask GitHub and what the answer means (P06-03, ADR 0032). Deterministic clock, scripted source.</summary>
public sealed class UpdateCheckerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly SemanticVersion Installed = new(1, 2, 0, null);

    private readonly ManualTimeProvider _time = new(Start);
    private readonly ScriptedSource _source = new();
    private readonly MemoryStore _store = new();
    private AppSettings _settings = WithConsent(ConsentState.Granted);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_it_managed_copy_never_asks_github_not_even_manually()
    {
        using var checker = Create(new UpdatePolicy(true, UpdatePolicySource.MachinePolicy));

        Assert.Equal(UpdateCheckOutcome.DisabledByPolicy, (await checker.CheckAsync(manual: false, Ct)).Outcome);
        Assert.Equal(UpdateCheckOutcome.DisabledByPolicy, (await checker.CheckAsync(manual: true, Ct)).Outcome);
        Assert.Equal(0, _source.Requests);
    }

    [Fact]
    public async Task Automatic_checks_need_consent_but_check_now_does_not()
    {
        _settings = WithConsent(ConsentState.NotAsked);
        using var checker = Create();
        _source.Next = Found("1.3.0");

        Assert.Equal(UpdateCheckOutcome.NotAllowed, (await checker.CheckAsync(manual: false, Ct)).Outcome);
        Assert.Equal(0, _source.Requests);

        var manual = await checker.CheckAsync(manual: true, Ct);
        Assert.Equal(UpdateCheckOutcome.UpdateAvailable, manual.Outcome);
        Assert.Equal("1.3.0", manual.Latest!.Version.ToString());
        Assert.Equal(1, _source.Requests);
    }

    [Theory]
    [InlineData("1.3.0", UpdateCheckOutcome.UpdateAvailable)]
    [InlineData("1.2.0", UpdateCheckOutcome.UpToDate)]
    [InlineData("1.1.9", UpdateCheckOutcome.UpToDate)] // Never "update" to an older version.
    public async Task Only_a_newer_release_is_an_update(string latest, UpdateCheckOutcome expected)
    {
        using var checker = Create();
        _source.Next = Found(latest);

        Assert.Equal(expected, (await checker.CheckAsync(manual: false, Ct)).Outcome);
    }

    [Fact]
    public async Task Automatic_checks_run_once_a_day_plus_the_jitter()
    {
        using var checker = Create(jitter: TimeSpan.FromMinutes(30));
        _source.Next = Found("1.2.0");
        await checker.CheckAsync(manual: false, Ct);

        _time.Advance(TimeSpan.FromHours(24));
        Assert.Equal(UpdateCheckOutcome.NotDue, (await checker.CheckAsync(manual: false, Ct)).Outcome);
        Assert.Equal(Start + TimeSpan.FromHours(24.5), checker.NextAutomaticCheckUtc);

        _time.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(UpdateCheckOutcome.UpToDate, (await checker.CheckAsync(manual: false, Ct)).Outcome);
        Assert.Equal(2, _source.Requests);
    }

    [Fact]
    public async Task Failures_back_off_doubling_from_one_hour_and_success_resets()
    {
        using var checker = Create();
        _source.Next = new ReleaseFetch(ReleaseFetchStatus.Unreachable);

        Assert.Equal(UpdateCheckOutcome.Failed, (await checker.CheckAsync(manual: false, Ct)).Outcome);
        Assert.Equal(Start + TimeSpan.FromHours(1), checker.NextAutomaticCheckUtc);

        _time.Advance(TimeSpan.FromHours(1));
        await checker.CheckAsync(manual: false, Ct);
        Assert.Equal(_time.UtcNow + TimeSpan.FromHours(2), checker.NextAutomaticCheckUtc);

        _time.Advance(TimeSpan.FromHours(2));
        _source.Next = Found("1.2.0");
        Assert.Equal(UpdateCheckOutcome.UpToDate, (await checker.CheckAsync(manual: false, Ct)).Outcome);
        Assert.Equal(_time.UtcNow + UpdateChecker.Interval, checker.NextAutomaticCheckUtc);
    }

    [Fact]
    public async Task Github_rate_limits_are_respected_even_for_check_now()
    {
        using var checker = Create();
        _source.Next = new ReleaseFetch(ReleaseFetchStatus.RateLimited, RetryAfterUtc: Start + TimeSpan.FromMinutes(40));

        Assert.Equal(UpdateCheckOutcome.RateLimited, (await checker.CheckAsync(manual: true, Ct)).Outcome);
        _time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(UpdateCheckOutcome.RateLimited, (await checker.CheckAsync(manual: true, Ct)).Outcome);
        Assert.Equal(1, _source.Requests);

        _time.Advance(TimeSpan.FromMinutes(40));
        _source.Next = Found("1.2.0");
        Assert.Equal(UpdateCheckOutcome.UpToDate, (await checker.CheckAsync(manual: true, Ct)).Outcome);
        Assert.Equal(2, _source.Requests);
    }

    [Fact]
    public async Task Check_now_twice_within_a_minute_asks_github_once()
    {
        using var checker = Create();
        _source.Next = Found("1.3.0");

        await checker.CheckAsync(manual: true, Ct);
        _time.Advance(TimeSpan.FromSeconds(20));
        var again = await checker.CheckAsync(manual: true, Ct);

        Assert.Equal(UpdateCheckOutcome.UpdateAvailable, again.Outcome);
        Assert.Equal(1, _source.Requests);
    }

    [Fact]
    public async Task The_etag_is_sent_and_an_unchanged_answer_keeps_the_cached_release()
    {
        using var checker = Create();
        _source.Next = Found("1.3.0") with { ETag = "\"abc\"" };
        await checker.CheckAsync(manual: false, Ct);

        _time.Advance(UpdateChecker.Interval + TimeSpan.FromMinutes(1));
        _source.Next = new ReleaseFetch(ReleaseFetchStatus.NotModified, ETag: "\"abc\"");
        var result = await checker.CheckAsync(manual: false, Ct);

        Assert.Equal("\"abc\"", _source.LastETag);
        Assert.Equal(UpdateCheckOutcome.UpdateAvailable, result.Outcome);
        Assert.Equal("1.3.0", result.Latest!.Version.ToString());
        Assert.Equal(_time.UtcNow, result.CheckedUtc);
    }

    [Fact]
    public async Task An_untrusted_answer_fails_and_forgets_the_etag()
    {
        using var checker = Create();
        _source.Next = Found("1.3.0") with { ETag = "\"abc\"" };
        await checker.CheckAsync(manual: true, Ct);

        _time.Advance(TimeSpan.FromMinutes(2));
        _source.Next = new ReleaseFetch(ReleaseFetchStatus.Invalid);
        Assert.Equal(UpdateCheckOutcome.Failed, (await checker.CheckAsync(manual: true, Ct)).Outcome);

        _time.Advance(TimeSpan.FromMinutes(2));
        _source.Next = Found("1.3.0");
        await checker.CheckAsync(manual: true, Ct);
        Assert.Null(_source.LastETag); // A full request again.
    }

    [Fact]
    public async Task No_published_release_is_its_own_answer_and_the_state_survives_a_restart()
    {
        using (var first = Create())
        {
            _source.Next = new ReleaseFetch(ReleaseFetchStatus.NoRelease);
            Assert.Equal(UpdateCheckOutcome.NoReleases, (await first.CheckAsync(manual: false, Ct)).Outcome);
        }

        using var restarted = Create();
        _time.Advance(TimeSpan.FromHours(3));
        Assert.Equal(UpdateCheckOutcome.NotDue, (await restarted.CheckAsync(manual: false, Ct)).Outcome);
        Assert.Equal(1, _source.Requests);
    }

    [Fact]
    public async Task A_new_version_is_announced_once_even_across_restarts()
    {
        using (var first = Create())
        {
            Assert.Null(first.TakeNotice()); // Nothing checked yet.
            _source.Next = Found("1.3.0");
            await first.CheckAsync(manual: false, Ct);

            Assert.Equal("1.3.0", first.TakeNotice()!.Version.ToString());
            Assert.Null(first.TakeNotice());
        }

        using var restarted = Create();
        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(UpdateCheckOutcome.NotDue, (await restarted.CheckAsync(manual: false, Ct)).Outcome);
        Assert.Null(restarted.TakeNotice()); // The cached answer, already announced.

        _time.Advance(UpdateChecker.Interval);
        _source.Next = Found("1.4.0");
        await restarted.CheckAsync(manual: false, Ct);
        Assert.Equal("1.4.0", restarted.TakeNotice()!.Version.ToString());
    }

    [Fact]
    public async Task A_cached_unannounced_update_is_announced_after_a_restart()
    {
        _store.Save(UpdateCheckState.Empty with { LastAttemptUtc = Start, LastSuccessUtc = Start, Latest = Found("1.3.0").Release });
        using var checker = Create();

        Assert.Equal(UpdateCheckOutcome.NotDue, (await checker.CheckAsync(manual: false, Ct)).Outcome);
        Assert.Equal("1.3.0", checker.TakeNotice()!.Version.ToString());
        Assert.Equal(0, _source.Requests);
    }

    [Fact]
    public async Task Being_up_to_date_announces_nothing()
    {
        using var checker = Create();
        _source.Next = Found("1.2.0");
        await checker.CheckAsync(manual: false, Ct);

        Assert.Null(checker.TakeNotice());
    }

    [Fact]
    public void Messages_say_what_happened_in_plain_words()
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        var release = new ReleaseInfo(new SemanticVersion(1, 3, 0, null), "v1.3.0", "Dynamic Island 1.3.0", Start, new Uri("https://github.com/x/y/releases/tag/v1.3.0"), string.Empty, []);

        Assert.StartsWith("Version 1.3.0 is available (released 11/3/2026); you have 1.2.0.",
            UpdateMessages.Describe(new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, release, Start), Installed, TimeZoneInfo.Utc, culture), StringComparison.Ordinal);
        Assert.Equal("You have the latest version, 1.2.0 (checked 11/3/2026 9:00 AM).",
            UpdateMessages.Describe(new UpdateCheckResult(UpdateCheckOutcome.UpToDate, release, Start), Installed, TimeZoneInfo.Utc, culture));
        Assert.StartsWith("Version 1.3.0 is available", // A cached answer after a restart.
            UpdateMessages.Describe(new UpdateCheckResult(UpdateCheckOutcome.NotDue, release, Start), Installed, TimeZoneInfo.Utc, culture), StringComparison.Ordinal);
        Assert.Null(UpdateMessages.Newer(new UpdateCheckResult(UpdateCheckOutcome.NotDue, release, Start), new SemanticVersion(1, 3, 0, null)));
        Assert.Contains("Couldn't reach GitHub", UpdateMessages.Describe(new UpdateCheckResult(UpdateCheckOutcome.Failed), Installed, TimeZoneInfo.Utc, culture), StringComparison.Ordinal);
    }

    private UpdateChecker Create(UpdatePolicy? policy = null, TimeSpan jitter = default) =>
        new(_source, _store, () => _settings, policy ?? UpdatePolicy.Allowed, Installed, _time, jitter);

    private static AppSettings WithConsent(ConsentState consent) => new() { Updates = new UpdateSettings { AutomaticChecks = consent } };

    private static ReleaseFetch Found(string version) => new(
        ReleaseFetchStatus.Found,
        new ReleaseInfo(SemanticVersion.TryParse(version)!, "v" + version, "Dynamic Island " + version, Start, new Uri("https://github.com/x/y/releases/tag/v" + version), string.Empty, []));

    private sealed class ScriptedSource : IReleaseSource
    {
        public ReleaseFetch Next { get; set; } = new(ReleaseFetchStatus.Unreachable);

        public int Requests { get; private set; }

        public string? LastETag { get; private set; }

        public Task<ReleaseFetch> FetchLatestAsync(string? etag, CancellationToken cancellationToken)
        {
            Requests++;
            LastETag = etag;
            return Task.FromResult(Next);
        }
    }

    private sealed class MemoryStore : IUpdateStateStore
    {
        private UpdateCheckState _state = UpdateCheckState.Empty;

        public UpdateCheckState Load() => _state;

        public void Save(UpdateCheckState state) => _state = state;
    }
}
