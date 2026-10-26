using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Notifications;

namespace DynamicIsland.Core.Components;

/// <summary>
/// Error boundary for one <see cref="IIsolatedComponent"/> (guide §4.2, §4.5). Runs it on an observed background task;
/// transient failures retry forever with capped backoff, recoverable ones reinitialize up to
/// <see cref="RetryPolicy.MaxRecoverableAttempts"/> times then mark it <see cref="ComponentState.Failed"/>, fatal ones
/// request a fatal app shutdown. A component never brings the app down through an ordinary exception.
/// </summary>
public sealed class ComponentBoundary : IComponentContext, IDisposable
{
    private readonly IIsolatedComponent _component;
    private readonly LifecycleCoordinator _lifecycle;
    private readonly INotificationService _notices;
    private readonly IFailureClassifier _classifier;
    private readonly RetryPolicy _policy;
    private readonly TimeProvider _timeProvider;
    private readonly Func<double> _random;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _stopping;
    private Task? _loop;
    private ComponentStatus _status;

    public ComponentBoundary(
        IIsolatedComponent component,
        LifecycleCoordinator lifecycle,
        INotificationService notices,
        IFailureClassifier classifier,
        RetryPolicy policy,
        TimeProvider timeProvider,
        Func<double>? random = null)
    {
        ArgumentNullException.ThrowIfNull(component);
        ReasonCodes.Validate(component.Name);

        _component = component;
        _lifecycle = lifecycle;
        _notices = notices;
        _classifier = classifier;
        _policy = policy;
        _timeProvider = timeProvider;
        _random = random ?? Random.Shared.NextDouble;
        _status = new ComponentStatus(component.Name, ComponentState.NotStarted);
    }

    /// <summary>Raised on the component's background thread whenever its status changes.</summary>
    public event EventHandler<ComponentStatus>? StatusChanged;

    public string Name => _component.Name;

    public ComponentStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_loop is { IsCompleted: false })
            {
                throw new InvalidOperationException($"Component '{Name}' is already running.");
            }

            // Restartable after a stop (pause/resume): each run gets a fresh cancellation source.
            _stopping?.Dispose();
            _stopping = CancellationTokenSource.CreateLinkedTokenSource(_lifecycle.ApplicationStopping);
            var token = _stopping.Token;
            _loop = Task.Run(() => RunLoopAsync(token), CancellationToken.None);
        }
    }

    /// <summary>Marks the component as stopped because the user paused the island.</summary>
    public void MarkPaused() => SetStatus(Status with { State = ComponentState.Stopped, ReasonCode = "paused", NextRetryUtc = null, ConsecutiveFailures = 0 });

    /// <summary>Marks the component as not started because of Safe Mode.</summary>
    public void MarkSkippedForSafeMode() => SetStatus(_status with { State = ComponentState.SkippedSafeMode, ReasonCode = "safe-mode" });

    /// <summary>Cancels the component and waits for it, bounded by <paramref name="cancellationToken"/>.</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? loop;
        lock (_gate)
        {
            loop = _loop;
        }

        if (loop is null)
        {
            return;
        }

        await _stopping!.CancelAsync().ConfigureAwait(false);
        try
        {
            await loop.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus(Status with { State = ComponentState.Stopped, ReasonCode = ReasonCodes.StopTimedOut });
            throw;
        }
    }

    public void ReportHealthy() => SetStatus(new ComponentStatus(Name, ComponentState.Healthy));

    public void ReportDegraded(string reasonCode)
    {
        ReasonCodes.Validate(reasonCode);
        SetStatus(Status with { State = ComponentState.Degraded, ReasonCode = reasonCode, NextRetryUtc = null });
    }

    public void Dispose() => _stopping?.Dispose();

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (failures == 0)
            {
                SetStatus(new ComponentStatus(Name, ComponentState.Starting));
            }

            try
            {
                await _component.RunAsync(this, cancellationToken).ConfigureAwait(false);
                SetStatus(Status with { State = ComponentState.Stopped, NextRetryUtc = null });
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Boundary by design: every failure is classified and handled here, never rethrown to the app.
                // ReportHealthy resets the count, so failures only accumulate while the component keeps failing.
                failures = Status.ConsecutiveFailures + 1;
                if (!await HandleFailureAsync(ex, failures, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
        }

        SetStatus(Status with { State = ComponentState.Stopped, NextRetryUtc = null });
    }

    /// <returns><c>true</c> to run the component again.</returns>
    private async Task<bool> HandleFailureAsync(Exception exception, int failures, CancellationToken cancellationToken)
    {
        var kind = _classifier.Classify(exception);
        var errorType = exception.GetType().FullName;
        var reason = exception is ComponentFailureException explicitFailure ? explicitFailure.ReasonCode : DefaultReason(kind);

        if (kind == FailureKind.Fatal)
        {
            SetStatus(new ComponentStatus(Name, ComponentState.Failed, reason, failures, null, errorType));
            _notices.Publish(new UserNotice(NoticeSeverity.Error, "component.fatal", Name));
            _lifecycle.RequestShutdown(ExitReason.Fatal);
            return false;
        }

        if (kind == FailureKind.Recoverable && failures > _policy.MaxRecoverableAttempts)
        {
            SetStatus(new ComponentStatus(Name, ComponentState.Failed, reason, failures, null, errorType));
            _notices.Publish(new UserNotice(NoticeSeverity.Warning, "component.failed", Name, "open-diagnostics"));
            return false;
        }

        var delay = _policy.GetDelay(failures, _random());
        SetStatus(new ComponentStatus(Name, ComponentState.Degraded, reason, failures, _timeProvider.GetUtcNow() + delay, errorType));
        if (kind == FailureKind.Transient && failures == _policy.TransientNoticeThreshold)
        {
            _notices.Publish(new UserNotice(NoticeSeverity.Info, "component.retrying", Name));
        }

        try
        {
            await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static string DefaultReason(FailureKind kind) => kind switch
    {
        FailureKind.Transient => ReasonCodes.TransientFailure,
        FailureKind.Recoverable => ReasonCodes.RecoverableFailure,
        _ => ReasonCodes.FatalFailure,
    };

    private void SetStatus(ComponentStatus status)
    {
        lock (_gate)
        {
            if (_status == status)
            {
                return;
            }

            _status = status;
        }

        StatusChanged?.Invoke(this, status);
    }
}
