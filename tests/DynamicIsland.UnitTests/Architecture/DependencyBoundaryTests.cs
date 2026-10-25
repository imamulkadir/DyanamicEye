using System.Reflection;
using System.Runtime.Versioning;
using System.Xml.Linq;

namespace DynamicIsland.UnitTests.Architecture;

/// <summary>
/// Enforces the dependency direction from guide §3: Core knows no WPF/Windows APIs; Widgets, Platform.Windows and
/// Infrastructure depend only on Core; App composes; Launcher and Updater stay separate processes without UI/OS-adapter dependencies.
/// </summary>
public sealed class DependencyBoundaryTests
{
    private const string Core = "DynamicIsland.Core";

    private static readonly Dictionary<string, string[]> AllowedProjectReferences = new()
    {
        [Core] = [],
        ["DynamicIsland.Widgets"] = [Core],
        ["DynamicIsland.Platform.Windows"] = [Core],
        ["DynamicIsland.Infrastructure"] = [Core],
        ["DynamicIsland.App"] = [Core, "DynamicIsland.Widgets", "DynamicIsland.Platform.Windows", "DynamicIsland.Infrastructure"],
        // Launcher reuses crash policy (Core) and crash store/logging (Infrastructure); never UI or OS adapters (ADR 0006).
        ["DynamicIsland.Launcher"] = [Core, "DynamicIsland.Infrastructure"],
        ["DynamicIsland.Updater"] = [],
    };

    private static readonly string[] ForbiddenCoreAssemblyPrefixes =
    [
        "PresentationCore",
        "PresentationFramework",
        "WindowsBase",
        "System.Xaml",
        "System.Windows.Forms",
        "Microsoft.Windows.SDK.NET",
        "WinRT.Runtime",
    ];

    private static readonly string[] ForbiddenCoreProperties = ["UseWPF", "UseWindowsForms", "UseWinUI"];

    public static TheoryData<string> SourceProjects => new(AllowedProjectReferences.Keys);

    [Fact]
    public void Every_source_project_has_a_dependency_rule()
    {
        var actual = Directory.GetDirectories(RepositoryPaths.Source)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(AllowedProjectReferences.Keys.Order(StringComparer.Ordinal), actual);
    }

    [Theory]
    [MemberData(nameof(SourceProjects))]
    public void Project_references_follow_dependency_direction(string projectName)
    {
        var references = LoadProject(projectName)
            .Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension((string?)r.Attribute("Include") ?? string.Empty))
            .ToArray();

        var disallowed = references.Except(AllowedProjectReferences[projectName], StringComparer.Ordinal);

        Assert.Empty(disallowed);
    }

    [Fact]
    public void Core_project_targets_portable_framework_without_ui_or_package_dependencies()
    {
        var project = LoadProject(Core);

        Assert.Equal("net10.0", project.Descendants("TargetFramework").Single().Value);
        Assert.Empty(project.Descendants("TargetFrameworks"));
        Assert.Empty(project.Descendants("FrameworkReference"));
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.All(ForbiddenCoreProperties, property => Assert.Empty(project.Descendants(property)));
    }

    [Fact]
    public void Core_assembly_references_no_windows_ui_assemblies_and_is_platform_neutral()
    {
        var core = Assembly.Load(Core);

        var forbidden = core.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => ForbiddenCoreAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));

        Assert.Empty(forbidden);
        Assert.Null(core.GetCustomAttribute<SupportedOSPlatformAttribute>());
    }

    private static XDocument LoadProject(string projectName) => XDocument.Load(RepositoryPaths.ProjectFile(projectName));
}
