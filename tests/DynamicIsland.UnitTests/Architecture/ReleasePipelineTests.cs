using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DynamicIsland.UnitTests.Architecture;

/// <summary>
/// Supply-chain rules for the CI and release workflows (P06-09, ADR 0038): third-party actions pinned by commit SHA,
/// the release signing key never used in CI, drafts only, and release notes present for the current version.
/// </summary>
public sealed partial class ReleasePipelineTests
{
    private static string Workflows => Path.Combine(RepositoryPaths.Root, ".github", "workflows");

    private static string ReleaseWorkflow => File.ReadAllText(Path.Combine(Workflows, "release.yml"));

    public static TheoryData<string> WorkflowFiles => new(Directory.GetFiles(Workflows, "*.yml").Select(Path.GetFileName)!);

    [Theory]
    [MemberData(nameof(WorkflowFiles))]
    public void Every_action_is_pinned_by_full_commit_sha(string file)
    {
        var uses = UsesLine().Matches(File.ReadAllText(Path.Combine(Workflows, file))).Select(m => m.Groups[1].Value).ToArray();

        Assert.NotEmpty(uses);
        Assert.All(uses, use => Assert.Matches(PinnedAction(), use));
    }

    [Fact]
    public void Release_workflow_never_touches_the_signing_key_and_only_creates_drafts()
    {
        var workflow = ReleaseWorkflow;

        Assert.DoesNotContain("RELEASE_SIGNING_KEY", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets.", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("New-ReleaseManifest", workflow, StringComparison.Ordinal);
        Assert.Contains("'--draft'", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--draft=false", workflow, StringComparison.Ordinal);
        Assert.Matches(@"tags:\s*\[""v\*""\]", workflow);
    }

    [Fact]
    public void Release_workflow_tests_before_packaging_and_attests_what_it_uploads()
    {
        var workflow = ReleaseWorkflow;
        int Step(string text) => workflow.IndexOf(text, StringComparison.Ordinal);

        Assert.True(Step("--locked-mode") > 0);
        Assert.True(Step("Test (Release)") < Step("Package (portable ZIP"));
        Assert.True(Step("Test (Debug") < Step("Package (portable ZIP"));
        Assert.True(Step("Package (portable ZIP") < Step("Attest build provenance"));
        Assert.True(Step("Attest build provenance") < Step("Create draft release"));
        Assert.Contains("artifacts/DynamicIsland-Portable-win-x64.zip", workflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/DynamicIsland-Setup-win-x64.exe", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Changelog_has_notes_for_the_next_release()
    {
        var changelog = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "CHANGELOG.md"));
        var version = XDocument.Load(Path.Combine(RepositoryPaths.Root, "Directory.Build.props")).Descendants("Version").First().Value;

        Assert.True(
            changelog.Contains("## [Unreleased]", StringComparison.Ordinal) || changelog.Contains($"## [{version}]", StringComparison.Ordinal),
            $"CHANGELOG.md needs an [Unreleased] or [{version}] section.");
    }

    [GeneratedRegex(@"^\s*(?:-\s+)?uses:\s*(\S+)", RegexOptions.Multiline)]
    private static partial Regex UsesLine();

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+@[0-9a-f]{40}$")]
    private static partial Regex PinnedAction();
}
