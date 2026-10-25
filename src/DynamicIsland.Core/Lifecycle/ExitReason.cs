namespace DynamicIsland.Core.Lifecycle;

/// <summary>Why the app is shutting down. Only <see cref="Fatal"/> leaves the session marked unhealthy.</summary>
public enum ExitReason
{
    /// <summary>User chose Exit or closed the app.</summary>
    UserExit,

    /// <summary>Windows sign-out, restart or shutdown.</summary>
    SessionEnding,

    /// <summary>Deliberate restart (e.g. after Safe Mode toggle or update).</summary>
    Restart,

    /// <summary>Unrecoverable error; the launcher may treat this as a crash.</summary>
    Fatal,
}

/// <summary>Process exit codes understood by the launcher (P01-04).</summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int Fatal = 1;
    public const int RestartRequested = 3;

    public static int For(ExitReason reason) => reason switch
    {
        ExitReason.Fatal => Fatal,
        ExitReason.Restart => RestartRequested,
        _ => Success,
    };
}
