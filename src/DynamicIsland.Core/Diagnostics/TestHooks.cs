namespace DynamicIsland.Core.Diagnostics;

/// <summary>
/// Environment switches used only by automated tests (fault injection, isolated data folder, no dialogs).
/// Compiled in only when <c>DYNAMICISLAND_TEST_HOOKS</c> is defined (Debug builds); Release builds always return
/// <c>null</c>, so shipped binaries cannot be steered through these variables.
/// </summary>
public static class TestHooks
{
    /// <summary><c>ui</c>, <c>background</c> or <c>startup</c>: make the app crash on purpose.</summary>
    public const string CrashVariable = "DYNAMICISLAND_TEST_CRASH";

    /// <summary>Folder used instead of <c>%LOCALAPPDATA%\DynamicIsland</c>.</summary>
    public const string DataRootVariable = "DYNAMICISLAND_TEST_DATA_ROOT";

    /// <summary><c>1</c>: suppress modal dialogs (launcher recovery message).</summary>
    public const string NoDialogsVariable = "DYNAMICISLAND_TEST_NO_DIALOGS";

    public static string? Get(string variable)
    {
#if DYNAMICISLAND_TEST_HOOKS
        return Environment.GetEnvironmentVariable(variable);
#else
        _ = variable;
        return null;
#endif
    }
}
