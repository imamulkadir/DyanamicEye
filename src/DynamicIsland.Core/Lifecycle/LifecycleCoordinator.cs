namespace DynamicIsland.Core.Lifecycle;

public enum ShutdownStepStatus
{
    Completed,
    TimedOut,
    Faulted,

    /// <summary>Not started because the total shutdown budget was exhausted.</summary>
    Skipped,
}

/// <param name="ErrorType">Exception type name for <see cref="ShutdownStepStatus.Faulted"/>; never the message.</param>
public sealed record ShutdownStepResult(string Name, ShutdownStepStatus Status, TimeSpan Elapsed, string? ErrorType = null);

public sealed record ShutdownReport(ExitReason Reason, int ExitCode, IReadOnlyList<ShutdownStepResult> Steps);

public sealed class ShutdownRequestedEventArgs(ExitReason reason, int exitCode) : EventArgs
{
    public ExitReason Reason { get; } = reason;

    public int ExitCode { get; } = exitCode;
}

/// <summary>
/// Single owner of app shutdown (guide §4.1, §4.5). The first shutdown request wins and cancels
/// <see cref="ApplicationStopping"/>; registered components then stop in reverse registration order, each bounded by
/// a step timeout and all by a total budget. A slow or failing component is reported, never allowed to block exit.
/// </summary>
public sealed class LifecycleCoordinator : IDisposable
{
    public static readonly TimeSpan DefaultStepTimeout = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan DefaultTotalBudget = TimeSpan.FromSeconds(5);

    private readonly Lock _gate = new();
    private readonly List<(string Name, Func<CancellationToken, Task> StopAsync)> _components = [];
    private readonly CancellationTokenSource _stopping = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _stepTimeout;
    private readonly TimeSpan _totalBudget;

    private ExitReason? _reason;
    private int _exitCode;
    private Task<ShutdownReport>? _shutdown;

    public LifecycleCoordinator(TimeProvider timeProvider)
        : this(timeProvider, DefaultStepTimeout, DefaultTotalBudget)
    {
    }

    public LifecycleCoordinator(TimeProvider timeProvider, TimeSpan stepTimeout, TimeSpan totalBudget)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(stepTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(totalBudget, stepTimeout);

        _timeProvider = timeProvider;
        _stepTimeout = stepTimeout;
        _totalBudget = totalBudget;
    }

    /// <summary>Raised once, on the requesting thread, when shutdown is first requested.</summary>
    public event EventHandler<ShutdownRequestedEventArgs>? ShutdownRequested;

    /// <summary>Cancelled when shutdown is requested; pass to all long-running work.</summary>
    public CancellationToken ApplicationStopping => _stopping.Token;

    public ExitReason? Reason
    {
        get
        {
            lock (_gate)
            {
                return _reason;
            }
        }
    }

    public int ExitCode
    {
        get
        {
            lock (_gate)
            {
                return _exitCode;
            }
        }
    }

    /// <returns><c>false</c> when shutdown was already requested (the first reason and exit code are kept).</returns>
    public bool RequestShutdown(ExitReason reason, int? exitCode = null)
    {
        int code;
        lock (_gate)
        {
            if (_reason is not null)
            {
                return false;
            }

            _reason = reason;
            _exitCode = code = exitCode ?? ExitCodes.For(reason);
        }

        _stopping.Cancel();
        ShutdownRequested?.Invoke(this, new ShutdownRequestedEventArgs(reason, code));
        return true;
    }

    /// <summary>Adds a component to stop during shutdown. Components stop in reverse registration order.</summary>
    public void Register(string name, Func<CancellationToken, Task> stopAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(stopAsync);

        lock (_gate)
        {
            if (_shutdown is not null)
            {
                throw new InvalidOperationException("Cannot register a component after shutdown has started.");
            }

            _components.Add((name, stopAsync));
        }
    }

    /// <summary>
    /// Stops all components once and reports the outcome. Requests <see cref="ExitReason.UserExit"/> if no reason was set.
    /// Never throws for component failures.
    /// </summary>
    public Task<ShutdownReport> ShutdownAsync()
    {
        RequestShutdown(ExitReason.UserExit);

        lock (_gate)
        {
            if (_shutdown is null)
            {
                var components = _components.ToArray();
                _shutdown = Task.Run(() => StopComponentsAsync(components));
            }

            return _shutdown;
        }
    }

    public void Dispose() => _stopping.Dispose();

    private async Task<ShutdownReport> StopComponentsAsync((string Name, Func<CancellationToken, Task> StopAsync)[] components)
    {
        var results = new List<ShutdownStepResult>(components.Length);
        var shutdownStarted = _timeProvider.GetTimestamp();

        for (var i = components.Length - 1; i >= 0; i--)
        {
            var (name, stopAsync) = components[i];
            var remaining = _totalBudget - _timeProvider.GetElapsedTime(shutdownStarted);
            if (remaining <= TimeSpan.Zero)
            {
                results.Add(new ShutdownStepResult(name, ShutdownStepStatus.Skipped, TimeSpan.Zero));
                continue;
            }

            results.Add(await StopComponentAsync(name, stopAsync, remaining < _stepTimeout ? remaining : _stepTimeout).ConfigureAwait(false));
        }

        lock (_gate)
        {
            return new ShutdownReport(_reason!.Value, _exitCode, results);
        }
    }

    private async Task<ShutdownStepResult> StopComponentAsync(string name, Func<CancellationToken, Task> stopAsync, TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout, _timeProvider);
        var started = _timeProvider.GetTimestamp();

        try
        {
            await stopAsync(timeoutSource.Token).WaitAsync(timeout, _timeProvider).ConfigureAwait(false);
            return new ShutdownStepResult(name, ShutdownStepStatus.Completed, _timeProvider.GetElapsedTime(started));
        }
        catch (Exception ex) when (ex is TimeoutException || (ex is OperationCanceledException && timeoutSource.IsCancellationRequested))
        {
            return new ShutdownStepResult(name, ShutdownStepStatus.TimedOut, _timeProvider.GetElapsedTime(started));
        }
        catch (Exception ex)
        {
            // Isolation by design: one broken component must not prevent the others from stopping or the app from exiting.
            return new ShutdownStepResult(name, ShutdownStepStatus.Faulted, _timeProvider.GetElapsedTime(started), ex.GetType().FullName);
        }
    }
}
