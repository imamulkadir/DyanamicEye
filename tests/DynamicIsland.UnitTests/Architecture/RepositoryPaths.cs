namespace DynamicIsland.UnitTests.Architecture;

internal static class RepositoryPaths
{
    private const string RootMarkerFile = "global.json";

    public static string Root { get; } = FindRoot();

    public static string Source => Path.Combine(Root, "src");

    public static string ProjectFile(string projectName) =>
        Path.Combine(Source, projectName, projectName + ".csproj");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, RootMarkerFile)))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"Repository root ({RootMarkerFile}) not found above {AppContext.BaseDirectory}.");
    }
}
