using System.IO;
using System.Security;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Startup;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.App;

/// <summary>
/// Keeps the sign-in Run entry in line with the user's consent (ADR 0015): on every start and whenever the consent
/// changes. Registry failures are logged by type and shown as a state, never thrown at the UI.
/// </summary>
public sealed class StartupCoordinator
{
    private readonly IStartupRegistration _registration;
    private readonly ISettingsService _settings;
    private readonly ILogger<StartupCoordinator> _logger;
    private readonly string? _command;
    private readonly Lock _gate = new();
    private ConsentState _lastConsent;

    /// <param name="launcherPath">This copy's launcher; <c>null</c> or missing means startup cannot be registered.</param>
    public StartupCoordinator(IStartupRegistration registration, ISettingsService settings, string? launcherPath, ILogger<StartupCoordinator> logger)
    {
        _registration = registration;
        _settings = settings;
        _logger = logger;
        _command = launcherPath is not null && File.Exists(launcherPath) ? StartupPolicy.BuildCommand(launcherPath) : null;
        _lastConsent = settings.Current.General.StartWithWindows;
        settings.Changed += OnSettingsChanged;
    }

    /// <summary>Raised after the registration may have changed (on the thread that changed it).</summary>
    public event EventHandler? StateChanged;

    /// <summary>What the settings window shows.</summary>
    public StartupState State
    {
        get
        {
            var status = TryGetStatus();
            return status is null ? StartupState.NotRegistered : StartupPolicy.Describe(_settings.Current.General.StartWithWindows, status, _command);
        }
    }

    /// <summary>Routine check at app start: register, remove a stale entry, or repair a moved copy.</summary>
    public void Reconcile()
    {
        lock (_gate)
        {
            _lastConsent = _settings.Current.General.StartWithWindows;
        }

        Reconcile(userJustEnabled: false);
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        var consent = settings.General.StartWithWindows;
        bool changed;
        lock (_gate)
        {
            changed = consent != _lastConsent;
            _lastConsent = consent;
        }

        if (changed)
        {
            Reconcile(userJustEnabled: consent == ConsentState.Granted);
        }
    }

    private void Reconcile(bool userJustEnabled)
    {
        lock (_gate)
        {
            var status = TryGetStatus();
            if (status is null)
            {
                return;
            }

            var consent = _settings.Current.General.StartWithWindows;
            var action = StartupPolicy.Decide(consent, status, _command, userJustEnabled, File.Exists);
            try
            {
                switch (action)
                {
                    case StartupAction.Register:
                        _registration.Register(_command!);
                        AppLog.StartupRegistered(_logger, repaired: status.IsRegistered);
                        break;
                    case StartupAction.Unregister:
                        _registration.Unregister();
                        AppLog.StartupRemoved(_logger);
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                AppLog.StartupRegistrationFailed(_logger, ex.GetType().Name);
            }

            if (consent == ConsentState.Granted)
            {
                var state = StartupPolicy.Describe(consent, TryGetStatus() ?? status, _command);
                if (state != StartupState.On)
                {
                    AppLog.StartupNotActive(_logger, state);
                }
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private StartupRegistrationStatus? TryGetStatus()
    {
        try
        {
            return _registration.GetStatus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            AppLog.StartupRegistrationFailed(_logger, ex.GetType().Name);
            return null;
        }
    }
}
