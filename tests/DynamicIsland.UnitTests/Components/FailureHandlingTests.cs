using DynamicIsland.Core.Components;
using DynamicIsland.Core.Notifications;
using DynamicIsland.UnitTests.TestSupport;

namespace DynamicIsland.UnitTests.Components;

public sealed class RetryPolicyTests
{
    private static readonly RetryPolicy Policy = new() { BaseDelay = TimeSpan.FromSeconds(1), MaxDelay = TimeSpan.FromMinutes(5), Jitter = 0.2 };

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(5, 16)]
    public void Delay_doubles_per_attempt(int attempt, double expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), Policy.GetDelay(attempt, random: 0.5));
    }

    [Fact]
    public void Delay_is_capped()
    {
        Assert.Equal(Policy.MaxDelay, Policy.GetDelay(50, random: 0.5));
        Assert.Equal(Policy.MaxDelay, Policy.GetDelay(50, random: 0.999999));
    }

    [Fact]
    public void Jitter_stays_within_bounds()
    {
        Assert.Equal(TimeSpan.FromSeconds(3.2), Policy.GetDelay(3, random: 0));
        Assert.True(Policy.GetDelay(3, random: 0.999999) <= TimeSpan.FromSeconds(4.8));
    }
}

public sealed class FailureClassifierTests
{
    private readonly DefaultFailureClassifier _classifier = new();

    [Fact]
    public void Exceptions_map_to_guide_error_classes()
    {
        Assert.Equal(FailureKind.Transient, _classifier.Classify(new TimeoutException()));
        Assert.Equal(FailureKind.Transient, _classifier.Classify(new IOException()));
        Assert.Equal(FailureKind.Transient, _classifier.Classify(new HttpRequestException()));
        Assert.Equal(FailureKind.Recoverable, _classifier.Classify(new InvalidOperationException()));
        Assert.Equal(FailureKind.Recoverable, _classifier.Classify(new ArgumentException()));
        Assert.Equal(FailureKind.Fatal, _classifier.Classify(new InsufficientExecutionStackException()));
        Assert.Equal(FailureKind.Fatal, _classifier.Classify(new ComponentFailureException(FailureKind.Fatal, "integrity")));
        Assert.Equal(FailureKind.Transient, _classifier.Classify(new ComponentFailureException(FailureKind.Transient, "offline")));
    }

    [Fact]
    public void Reason_codes_must_be_safe_identifiers()
    {
        Assert.Throws<ArgumentException>(() => new ComponentFailureException(FailureKind.Transient, "C:\\Users\\alice"));
        Assert.Throws<ArgumentException>(() => new ComponentFailureException(FailureKind.Transient, ""));
    }
}

public sealed class NotificationServiceTests
{
    private static readonly UserNotice Notice = new(NoticeSeverity.Warning, "component.failed", "media");

    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Duplicate_is_suppressed_within_the_window_and_allowed_after()
    {
        var service = new InMemoryNotificationService(_time);
        var published = 0;
        service.Published += (_, _) => published++;

        Assert.True(service.Publish(Notice));
        Assert.False(service.Publish(Notice with { }));
        _time.UtcNow += InMemoryNotificationService.DuplicateWindow;
        Assert.True(service.Publish(Notice));

        Assert.Equal(2, published);
        Assert.Single(service.Active);
    }

    [Fact]
    public void Burst_is_rate_limited_per_minute()
    {
        var service = new InMemoryNotificationService(_time);

        var accepted = Enumerable.Range(0, 10).Count(i => service.Publish(Notice with { Subject = $"widget{i}" }));

        Assert.Equal(InMemoryNotificationService.MaxPerMinute, accepted);
        _time.UtcNow += TimeSpan.FromMinutes(1);
        Assert.True(service.Publish(Notice with { Subject = "later" }));
    }

    [Fact]
    public void Active_list_is_bounded_and_dismissable()
    {
        var service = new InMemoryNotificationService(_time);
        for (var i = 0; i < InMemoryNotificationService.MaxActive + 5; i++)
        {
            _time.UtcNow += TimeSpan.FromMinutes(1);
            service.Publish(Notice with { Subject = $"widget{i}" });
        }

        Assert.Equal(InMemoryNotificationService.MaxActive, service.Active.Count);
        var newest = service.Active[^1];
        service.Dismiss(newest);
        Assert.DoesNotContain(newest, service.Active);
    }

    [Theory]
    [InlineData("Component failed: C:\\Users\\alice")]
    [InlineData("")]
    [InlineData("UPPER.case")]
    public void Free_text_is_rejected(string key)
    {
        var service = new InMemoryNotificationService(_time);

        Assert.Throws<ArgumentException>(() => service.Publish(new UserNotice(NoticeSeverity.Info, key)));
    }
}
