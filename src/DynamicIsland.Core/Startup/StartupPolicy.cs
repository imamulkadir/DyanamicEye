using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.Core.Startup;

/// <summary>What Windows has for our sign-in entry (guide §3 <c>IStartupRegistration</c>, ADR 0015).</summary>
/// <param name="Command">Our Run value, or <c>null</c> when there is none.</param>
/// <param name="DisabledInWindows">Turned off in Settings › Apps › Startup or Task Manager (StartupApproved); never changed by us.</param>
/// <param name="BlockedByPolicy">Per-user Run entries are disabled by policy (<c>DisableCurrentUserRun</c>).</param>
public sealed record StartupRegistrationStatus(string? Command, bool DisabledInWindows, bool BlockedByPolicy)
{
    public static StartupRegistrationStatus None { get; } = new(null, false, false);

    public bool IsRegistered => Command is not null;
}

/// <summary>Per-user sign-in registration (HKCU Run). Implementations never elevate and never touch policy or StartupApproved.</summary>
public interface IStartupRegistration
{
    StartupRegistrationStatus GetStatus();

    void Register(string command);

    /// <summary>Removes our Run value (no-op when absent).</summary>
    void Unregister();
}

public enum StartupAction
{
    None,
    Register,
    Unregister,
}

/// <summary>What the settings window shows about sign-in startup.</summary>
public enum StartupState
{
    Off,
    On,

    /// <summary>On here, but turned off in Windows (Settings › Apps › Startup or Task Manager).</summary>
    DisabledInWindows,

    /// <summary>Blocked by an administrator policy; nothing is written.</summary>
    BlockedByPolicy,

    /// <summary>The entry starts another copy of Dynamic Island that still exists.</summary>
    OtherCopy,

    /// <summary>No launcher next to the app, so nothing can be registered.</summary>
    LauncherMissing,

    /// <summary>On here, but the entry could not be written (see the log).</summary>
    NotRegistered,
}

/// <summary>Pure decisions for sign-in startup (ADR 0015 §4).</summary>
public static class StartupPolicy
{
    /// <summary>The Run value for a launcher: <c>"&lt;path&gt;" --startup</c>.</summary>
    public static string BuildCommand(string launcherPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launcherPath);
        if (launcherPath.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException("A path cannot contain quotes.", nameof(launcherPath));
        }

        return $"\"{launcherPath}\" {LaunchArguments.StartupSwitch}";
    }

    /// <summary>The executable of a Run value (quoted or not); <c>null</c> when it cannot be read.</summary>
    public static string? ExecutableOf(string? command)
    {
        var text = command?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text[0] == '"')
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }

        var space = text.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? text : text[..space];
    }

    /// <param name="consent">The user's sign-in startup choice.</param>
    /// <param name="status">Current registration.</param>
    /// <param name="command">The command for this copy, or <c>null</c> when this copy has no launcher.</param>
    /// <param name="userJustEnabled">The user switched startup on in this copy right now (not a routine check).</param>
    /// <param name="fileExists">Checks whether a registered launcher still exists.</param>
    public static StartupAction Decide(
        ConsentState consent,
        StartupRegistrationStatus status,
        string? command,
        bool userJustEnabled,
        Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(fileExists);

        if (consent != ConsentState.Granted)
        {
            return status.IsRegistered ? StartupAction.Unregister : StartupAction.None;
        }

        if (status.BlockedByPolicy || command is null)
        {
            return StartupAction.None;
        }

        if (!status.IsRegistered)
        {
            return StartupAction.Register;
        }

        if (string.Equals(status.Command, command, StringComparison.OrdinalIgnoreCase))
        {
            return StartupAction.None;
        }

        // Another copy is registered: take over only on the user's explicit choice, or repair a moved/deleted copy.
        var registered = ExecutableOf(status.Command);
        return userJustEnabled || registered is null || !fileExists(registered) ? StartupAction.Register : StartupAction.None;
    }

    public static StartupState Describe(ConsentState consent, StartupRegistrationStatus status, string? command)
    {
        ArgumentNullException.ThrowIfNull(status);

        if (consent != ConsentState.Granted)
        {
            return StartupState.Off;
        }

        if (status.BlockedByPolicy)
        {
            return StartupState.BlockedByPolicy;
        }

        if (command is null)
        {
            return StartupState.LauncherMissing;
        }

        if (!status.IsRegistered)
        {
            return StartupState.NotRegistered;
        }

        if (!string.Equals(status.Command, command, StringComparison.OrdinalIgnoreCase))
        {
            return StartupState.OtherCopy;
        }

        return status.DisabledInWindows ? StartupState.DisabledInWindows : StartupState.On;
    }
}
