namespace DynamicIsland.Infrastructure;

/// <summary>
/// Per-user data locations (guide §4.3). Data never lives next to the executable, installed or portable.
/// </summary>
public static class AppDataPaths
{
    public const string ProductFolderName = "DynamicIsland";

    /// <summary><c>%LOCALAPPDATA%\DynamicIsland</c>.</summary>
    public static string Root => Path.Combine(LocalApplicationData(), ProductFolderName);

    /// <summary><c>%LOCALAPPDATA%\DynamicIsland\data</c>: settings and database.</summary>
    public static string Data => Path.Combine(Root, "data");

    private static string LocalApplicationData()
    {
        var path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrEmpty(path))
        {
            throw new InvalidOperationException("The per-user local application data folder is unavailable.");
        }

        return path;
    }
}
