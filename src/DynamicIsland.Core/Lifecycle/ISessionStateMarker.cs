namespace DynamicIsland.Core.Lifecycle;

/// <summary>
/// Persists whether the app is running, so the next start can tell a clean exit from a crash, kill or power loss.
/// </summary>
public interface ISessionStateMarker
{
    /// <summary>Marks this session as running.</summary>
    /// <returns><c>true</c> when the previous session did not end cleanly.</returns>
    bool BeginSession();

    /// <summary>Clears the running mark. Call only for non-fatal exits.</summary>
    void EndSessionCleanly();
}
