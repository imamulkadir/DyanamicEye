namespace DynamicIsland.UnitTests.Architecture;

/// <summary>
/// The compatibility promises the installer makes (ADR 0001, 0031, P07-02): per-user without elevation, x64 only, and an
/// honest Windows 10 label that IT silent installs can pass.
/// </summary>
public sealed class InstallerScriptTests
{
    private static string Script => File.ReadAllText(Path.Combine(RepositoryPaths.Root, "packaging", "installer", "DynamicIsland.iss"));

    [Fact]
    public void Installs_per_user_for_x64_windows_10_2004_or_later()
    {
        var script = Script;

        Assert.Contains("PrivilegesRequired=lowest", script, StringComparison.Ordinal);
        Assert.Contains("ArchitecturesAllowed=x64compatible", script, StringComparison.Ordinal);
        Assert.Contains("MinVersion=10.0.19041", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_10_gets_an_honest_notice_that_silent_installs_can_pass()
    {
        var script = Script;

        Assert.Contains("Version.Build < 22000", script, StringComparison.Ordinal);
        Assert.Contains("Windows 10 Home and Pro are not supported", script, StringComparison.Ordinal);
        Assert.Contains("(best effort)", script, StringComparison.Ordinal);
        Assert.Contains("MB_YESNO or MB_DEFBUTTON2, IDYES) = IDYES", script, StringComparison.Ordinal); // /SUPPRESSMSGBOXES → Yes
    }
}
