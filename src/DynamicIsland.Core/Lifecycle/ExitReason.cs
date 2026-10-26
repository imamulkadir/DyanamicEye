namespace DynamicIsland.Core.Lifecycle;

/// <summary>Why the app is shutting down. Only <see cref="Fatal"/> leaves the session marked unhealthy.</summary>
public enum ExitReason
{
    /// <summary>User chose Exit or closed the app.</summary>
    UserExit,

    /// <summary>Windows sign-out, restart or shutdown.</summary>
    SessionEnding,

    /// <summary>Deliberate restart in normal mode (also "leave Safe Mode", later: after an update).</summary>
    Restart,

    /// <summary>Deliberate restart into Safe Mode (tray).</summary>
    RestartInSafeMode,

    /// <summary>Unrecoverable error; the launcher may treat this as a crash.</summary>
    Fatal,
}

/// <summary>Process exit codes understood by the launcher (P01-04, ADR 0006/0013).</summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int Fatal = 1;
    public const int RestartRequested = 3;
    public const int RestartInSafeModeRequested = 4;

    public static int For(ExitReason reason) => reason switch
    {
        ExitReason.Fatal => Fatal,
        ExitReason.Restart => RestartRequested,
        ExitReason.RestartInSafeMode => RestartInSafeModeRequested,
        _ => Success,
    };
}

/// <summary>Environment contract between launcher and app (not a security boundary: it only changes how restarts happen).</summary>
public static class LauncherContract
{
    /// <summary>Set to <c>1</c> by the launcher for the app it supervises: restarts are then done by exiting with 3/4.</summary>
    public const string SupervisedVariable = "DYNAMICISLAND_SUPERVISED";

    /// <summary>Launcher argument: wait for this process id to exit before starting the app (used by unsupervised restarts).</summary>
    public const string AfterPidSwitch = "--after-pid";

    public const string LauncherExecutable = "DynamicIsland.Launcher.exe";
}
