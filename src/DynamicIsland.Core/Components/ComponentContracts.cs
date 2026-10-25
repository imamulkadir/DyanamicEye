using System.Text.RegularExpressions;

namespace DynamicIsland.Core.Components;

/// <summary>
/// Long-running unit of optional work (widget data provider, OS adapter subscription). It runs until cancelled,
/// reports its health through <see cref="IComponentContext"/> and may throw: failures are contained by
/// <see cref="ComponentBoundary"/>, never propagated to the app.
/// </summary>
public interface IIsolatedComponent
{
    /// <summary>Stable, non-personal name used in logs, notices and diagnostics.</summary>
    string Name { get; }

    /// <summary>Runs until <paramref name="cancellationToken"/> is cancelled. Returning normally means "finished".</summary>
    Task RunAsync(IComponentContext context, CancellationToken cancellationToken);
}

public interface IComponentContext
{
    /// <summary>Marks the component healthy and resets its failure count.</summary>
    void ReportHealthy();

    /// <summary>Marks the component degraded (still running, e.g. no media session) with a stable reason code.</summary>
    void ReportDegraded(string reasonCode);
}

public enum ComponentState
{
    NotStarted,
    Starting,
    Healthy,
    Degraded,

    /// <summary>Gave up after repeated failures; stays stopped until restarted by the user or app.</summary>
    Failed,
    Stopped,

    /// <summary>Not started because the app runs in Safe Mode.</summary>
    SkippedSafeMode,
}

public enum FailureKind
{
    /// <summary>Offline, timeout, busy device: retry with backoff, indefinitely.</summary>
    Transient,

    /// <summary>Adapter disconnected or broken state: reinitialize a limited number of times.</summary>
    Recoverable,

    /// <summary>Process integrity in doubt: request a fatal shutdown.</summary>
    Fatal,
}

/// <param name="ReasonCode">Stable, non-personal code safe to show via notices (never an exception message).</param>
/// <param name="LastErrorType">Exception type name for logs and diagnostics only.</param>
public sealed record ComponentStatus(
    string Name,
    ComponentState State,
    string? ReasonCode = null,
    int ConsecutiveFailures = 0,
    DateTimeOffset? NextRetryUtc = null,
    string? LastErrorType = null);

/// <summary>Thrown by components to state the failure kind and a stable reason code explicitly.</summary>
public sealed class ComponentFailureException : Exception
{
    public ComponentFailureException(FailureKind kind, string reasonCode, Exception? innerException = null)
        : base($"Component failure ({kind}, {reasonCode}).", innerException)
    {
        ReasonCodes.Validate(reasonCode);
        Kind = kind;
        ReasonCode = reasonCode;
    }

    public ComponentFailureException()
        : this(FailureKind.Recoverable, ReasonCodes.RecoverableFailure)
    {
    }

    public ComponentFailureException(string message)
        : base(message)
    {
        Kind = FailureKind.Recoverable;
        ReasonCode = ReasonCodes.RecoverableFailure;
    }

    public ComponentFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
        Kind = FailureKind.Recoverable;
        ReasonCode = ReasonCodes.RecoverableFailure;
    }

    public FailureKind Kind { get; }

    public string ReasonCode { get; }
}

public static partial class ReasonCodes
{
    public const string TransientFailure = "transient-failure";
    public const string RecoverableFailure = "recoverable-failure";
    public const string FatalFailure = "fatal-failure";
    public const string StopTimedOut = "stop-timed-out";

    public static void Validate(string reasonCode)
    {
        if (reasonCode is null || !Pattern().IsMatch(reasonCode))
        {
            throw new ArgumentException("Reason codes are 1-64 lowercase letters, digits, '.' or '-'.", nameof(reasonCode));
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}

public interface IFailureClassifier
{
    FailureKind Classify(Exception exception);
}

/// <summary>Guide §4.5 error classes mapped from exception types; components can be explicit via <see cref="ComponentFailureException"/>.</summary>
public sealed class DefaultFailureClassifier : IFailureClassifier
{
    public FailureKind Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            ComponentFailureException explicitFailure => explicitFailure.Kind,
            OutOfMemoryException or InsufficientExecutionStackException or AccessViolationException => FailureKind.Fatal,
            TimeoutException or IOException or OperationCanceledException => FailureKind.Transient,
            _ when exception.GetType().FullName == "System.Net.Http.HttpRequestException" => FailureKind.Transient,
            _ => FailureKind.Recoverable,
        };
    }
}

/// <summary>Exponential backoff with jitter and caps (guide §4.2).</summary>
public sealed record RetryPolicy
{
    public static RetryPolicy Default { get; } = new();

    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Fraction of the delay randomly added or removed (0.2 = ±20%).</summary>
    public double Jitter { get; init; } = 0.2;

    /// <summary>Recoverable failures in a row before the component is marked <see cref="ComponentState.Failed"/>.</summary>
    public int MaxRecoverableAttempts { get; init; } = 3;

    /// <summary>Transient failures in a row before the user is notified (retries continue).</summary>
    public int TransientNoticeThreshold { get; init; } = 3;

    /// <param name="attempt">1-based failure count.</param>
    /// <param name="random">Uniform sample in [0, 1).</param>
    public TimeSpan GetDelay(int attempt, double random)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);

        var exponent = Math.Min(attempt - 1, 30);
        var raw = Math.Min(BaseDelay.TotalMilliseconds * Math.Pow(2, exponent), MaxDelay.TotalMilliseconds);
        var jittered = raw * (1 + (Jitter * ((2 * random) - 1)));
        return TimeSpan.FromMilliseconds(Math.Clamp(jittered, 0, MaxDelay.TotalMilliseconds));
    }
}
