using DynamicIsland.Core.Distribution;

namespace DynamicIsland.UnitTests.Distribution;

/// <summary>Release layout rules (P06-01, ADR 0030), on an in-memory file list.</summary>
public sealed class AppLayoutTests
{
    private const string Root = @"C:\Apps\DynamicIsland";
    private static readonly string Launcher = Path.Combine(Root, "DynamicIsland.exe");

    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("0.1.0", true)]
    [InlineData("1.0.0-beta.2", true)]
    [InlineData("10.20.30-rc1", true)]
    [InlineData("1.2", false)]
    [InlineData("01.2.3", false)]
    [InlineData("1.2.3-", false)]
    [InlineData("1.2.3+build", false)]
    [InlineData(@"..\..\evil", false)]
    [InlineData("1.2.3\\x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_semver_names_are_valid_versions(string? version, bool valid) =>
        Assert.Equal(valid, AppLayout.IsValidVersion(version));

    [Fact]
    public void Pointer_round_trips()
    {
        var pointer = new LayoutPointer("1.3.0", "1.2.0");

        Assert.Equal(pointer, AppLayout.ParsePointer(AppLayout.SerializePointer(pointer)));
        Assert.Equal(new LayoutPointer("1.3.0", null), AppLayout.ParsePointer(AppLayout.SerializePointer(new LayoutPointer("1.3.0", null))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "previous": "1.0.0" }""")]
    [InlineData("""{ "current": 3 }""")]
    [InlineData("""{ "current": "..\\app" }""")]
    public void Broken_pointers_are_rejected(string json) => Assert.Null(AppLayout.ParsePointer(json));

    [Fact]
    public void An_invalid_previous_version_is_dropped_not_fatal() =>
        Assert.Equal(new LayoutPointer("1.3.0", null), AppLayout.ParsePointer("""{ "current": "1.3.0", "previous": "../x" }"""));

    [Fact]
    public void Oversized_pointer_is_rejected() =>
        Assert.Null(AppLayout.ParsePointer("""{ "current": "1.3.0", "pad": " """ + new string(' ', AppLayout.MaxPointerBytes) + "\" }"));

    [Fact]
    public void Launcher_runs_the_current_payload()
    {
        var files = Files(@"app-1.3.0\DynamicIsland.exe", @"app-1.2.0\DynamicIsland.exe");

        var app = AppLayout.ResolveApp(Root, """{ "current": "1.3.0", "previous": "1.2.0" }""", files.Contains, Launcher);

        Assert.Equal(new ResolvedApp(Path.Combine(Root, "app-1.3.0", "DynamicIsland.exe"), "1.3.0", UsedPrevious: false), app);
    }

    [Fact]
    public void A_missing_current_payload_falls_back_to_the_previous_one()
    {
        var files = Files(@"app-1.2.0\DynamicIsland.exe"); // e.g. quarantined by antivirus or a failed update.

        var app = AppLayout.ResolveApp(Root, """{ "current": "1.3.0", "previous": "1.2.0" }""", files.Contains, Launcher);

        Assert.Equal(new ResolvedApp(Path.Combine(Root, "app-1.2.0", "DynamicIsland.exe"), "1.2.0", UsedPrevious: true), app);
    }

    [Fact]
    public void Nothing_to_run_gives_null()
    {
        Assert.Null(AppLayout.ResolveApp(Root, """{ "current": "1.3.0" }""", Files().Contains, Launcher));
        Assert.Null(AppLayout.ResolveApp(Root, """{ "current": "1.3.0", "previous": "1.2.0" }""", Files().Contains, Launcher));
    }

    [Fact]
    public void Without_a_pointer_the_app_next_to_the_launcher_runs_but_never_the_launcher_itself()
    {
        var devLauncher = Path.Combine(Root, "DynamicIsland.Launcher.exe");
        var files = Files("DynamicIsland.exe");

        Assert.Equal(Path.Combine(Root, "DynamicIsland.exe"), AppLayout.ResolveApp(Root, null, files.Contains, devLauncher)?.AppPath);

        // A root launcher named DynamicIsland.exe with a lost current.json must not start itself in a loop.
        Assert.Null(AppLayout.ResolveApp(Root, null, files.Contains, Launcher));
        Assert.Null(AppLayout.ResolveApp(Root, "garbage", files.Contains, Launcher));
    }

    [Fact]
    public void An_app_in_a_payload_folder_registers_the_stable_root_launcher()
    {
        var files = Files("current.json", "DynamicIsland.exe", @"app-1.3.0\DynamicIsland.exe");

        Assert.Equal(Launcher, AppLayout.LauncherFor(Path.Combine(Root, "app-1.3.0") + Path.DirectorySeparatorChar, files.Contains));
    }

    [Theory]
    [InlineData(@"C:\Apps\DynamicIsland\bin")] // Development layout.
    [InlineData(@"C:\Apps\DynamicIsland\app-latest")] // Not a version folder.
    public void Elsewhere_the_launcher_next_to_the_app_is_used(string appDirectory)
    {
        var files = Files("current.json", "DynamicIsland.exe");

        Assert.Equal(Path.Combine(appDirectory, "DynamicIsland.Launcher.exe"), AppLayout.LauncherFor(appDirectory, files.Contains));
    }

    [Fact]
    public void A_payload_folder_without_the_root_files_is_not_treated_as_a_layout() =>
        Assert.Equal(
            Path.Combine(Root, "app-1.3.0", "DynamicIsland.Launcher.exe"),
            AppLayout.LauncherFor(Path.Combine(Root, "app-1.3.0"), Files().Contains));

    private static HashSet<string> Files(params string[] relative) =>
        new(relative.Select(r => Path.Combine(Root, r)), StringComparer.OrdinalIgnoreCase);
}
