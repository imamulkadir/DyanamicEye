using System.Text.RegularExpressions;

namespace DynamicIsland.Core.Notifications;

public enum NoticeSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// Nonfatal message for the user (rendered by the shell/tray later). Carries stable keys only:
/// UI text comes from resources, and nothing personal or exception-derived is ever included.
/// </summary>
/// <param name="MessageKey">Resource key, e.g. <c>component.failed</c>.</param>
/// <param name="Subject">Optional non-personal subject, e.g. a component name.</param>
/// <param name="ActionId">Optional action the UI may offer, e.g. <c>open-diagnostics</c>.</param>
public sealed partial record UserNotice(NoticeSeverity Severity, string MessageKey, string? Subject = null, string? ActionId = null)
{
    public void Validate()
    {
        if (!KeyPattern().IsMatch(MessageKey)
            || (Subject is not null && !KeyPattern().IsMatch(Subject))
            || (ActionId is not null && !KeyPattern().IsMatch(ActionId)))
        {
            throw new ArgumentException("Notice keys, subjects and actions are 1-64 lowercase letters, digits, '.' or '-'.");
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}

public interface INotificationService
{
    event EventHandler<UserNotice>? Published;

    /// <summary>Notices currently shown (newest last).</summary>
    IReadOnlyList<UserNotice> Active { get; }

    /// <returns><c>false</c> when suppressed as a duplicate or by rate limiting.</returns>
    bool Publish(UserNotice notice);

    void Dismiss(UserNotice notice);
}

/// <summary>
/// Thread-safe notice queue. The same notice is suppressed for <see cref="DuplicateWindow"/>; at most
/// <see cref="MaxPerMinute"/> notices pass per minute; at most <see cref="MaxActive"/> stay active (oldest dropped).
/// </summary>
public sealed class InMemoryNotificationService(TimeProvider timeProvider) : INotificationService
{
    public const int MaxActive = 20;
    public const int MaxPerMinute = 5;

    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(10);

    private readonly Lock _gate = new();
    private readonly List<UserNotice> _active = [];
    private readonly Dictionary<UserNotice, DateTimeOffset> _lastPublished = [];
    private readonly Queue<DateTimeOffset> _recent = new();

    public event EventHandler<UserNotice>? Published;

    public IReadOnlyList<UserNotice> Active
    {
        get
        {
            lock (_gate)
            {
                return [.. _active];
            }
        }
    }

    public bool Publish(UserNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        notice.Validate();

        var now = timeProvider.GetUtcNow();
        lock (_gate)
        {
            if (_lastPublished.TryGetValue(notice, out var last) && now - last < DuplicateWindow)
            {
                return false;
            }

            while (_recent.Count > 0 && now - _recent.Peek() >= TimeSpan.FromMinutes(1))
            {
                _recent.Dequeue();
            }

            if (_recent.Count >= MaxPerMinute)
            {
                return false;
            }

            _recent.Enqueue(now);
            foreach (var expired in _lastPublished.Where(p => now - p.Value >= DuplicateWindow).Select(p => p.Key).ToArray())
            {
                _lastPublished.Remove(expired);
            }

            _lastPublished[notice] = now;
            _active.Remove(notice);
            _active.Add(notice);
            if (_active.Count > MaxActive)
            {
                _active.RemoveAt(0);
            }
        }

        Published?.Invoke(this, notice);
        return true;
    }

    public void Dismiss(UserNotice notice)
    {
        lock (_gate)
        {
            _active.Remove(notice);
        }
    }
}
