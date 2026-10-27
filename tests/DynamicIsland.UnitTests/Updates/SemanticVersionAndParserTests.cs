using DynamicIsland.Core.Updates;

namespace DynamicIsland.UnitTests.Updates;

/// <summary>SemVer precedence and the GitHub release parser (P06-03, ADR 0032).</summary>
public sealed class SemanticVersionAndParserTests
{
    private const string Repo = "imamulkadir/DyanamicEye";

    [Fact]
    public void Precedence_follows_the_semver_specification_example()
    {
        // SemVer 2.0 §11 example, plus core ordering.
        string[] ordered = ["1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.2.0", "2.0.0", "10.0.0"];
        var versions = ordered.Select(v => SemanticVersion.TryParse(v)!).ToArray();

        for (var i = 1; i < versions.Length; i++)
        {
            Assert.True(versions[i - 1] < versions[i], $"{versions[i - 1]} < {versions[i]}");
        }
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3+build.5", "1.2.3")] // Build metadata is ignored.
    [InlineData("v2.0.0-beta.1", "2.0.0-beta.1")]
    public void Tags_and_metadata_parse(string text, string expected) =>
        Assert.Equal(expected, SemanticVersion.TryParse(text)!.ToString());

    [Theory]
    [InlineData("1.2")]
    [InlineData("01.2.3")]
    [InlineData("1.2.3-")]
    [InlineData("vv1.2.3")]
    [InlineData("latest")]
    [InlineData("")]
    [InlineData(null)]
    public void Non_semver_is_rejected(string? text) => Assert.Null(SemanticVersion.TryParse(text));

    [Fact]
    public void Build_metadata_does_not_affect_equality_of_precedence() =>
        Assert.Equal(0, SemanticVersion.TryParse("1.2.3+a")!.CompareTo(SemanticVersion.TryParse("1.2.3+b")));

    [Fact]
    public void A_published_release_is_parsed_with_its_assets_and_plain_notes()
    {
        var release = GitHubReleaseParser.Parse(Release(), Repo);

        Assert.NotNull(release);
        Assert.Equal("1.3.0", release.Version.ToString());
        Assert.Equal("Dynamic Island 1.3.0", release.Title);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero), release.PublishedUtc);
        Assert.Equal($"https://github.com/{Repo}/releases/tag/v1.3.0", release.PageUrl.ToString());
        var asset = Assert.Single(release.Assets);
        Assert.Equal(("DynamicIsland-Setup-win-x64.exe", 51_000_000L), (asset.Name, asset.SizeBytes));
        Assert.Equal("## Features\n- <script>alert(1)</script> stays text", release.Notes);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("prerelease")]
    public void Drafts_and_prereleases_are_not_offered_on_the_stable_channel(string flag) =>
        Assert.Null(GitHubReleaseParser.Parse(Release(replace: (flag, "true")), Repo));

    [Theory]
    [InlineData("tag_name", "\"latest\"")]
    [InlineData("tag_name", "\"v2.0.0-beta.1\"")]
    [InlineData("html_url", "\"http://github.com/imamulkadir/DyanamicEye/releases/tag/v1.3.0\"")]
    [InlineData("html_url", "\"https://evil.example/imamulkadir/DyanamicEye/releases/tag/v1.3.0\"")]
    [InlineData("html_url", "\"https://github.com/someone/else/releases/tag/v1.3.0\"")]
    [InlineData("published_at", "\"yesterday\"")]
    public void Untrusted_release_fields_reject_the_release(string field, string value) =>
        Assert.Null(GitHubReleaseParser.Parse(Release(replace: (field, value)), Repo));

    [Theory]
    [InlineData("https://github.com/imamulkadir/DyanamicEye/releases/download/v1.3.0/x.exe", 0)]
    [InlineData("https://objects.githubusercontent.com/x.exe", 10)]
    [InlineData("https://github.com/imamulkadir/DyanamicEye/releases/download/../../x.exe", 10)]
    [InlineData("https://user:pass@github.com/imamulkadir/DyanamicEye/releases/download/v1.3.0/x.exe", 10)]
    public void One_untrusted_asset_rejects_the_whole_release(string url, long size)
    {
        var json = Release(assets: $$"""[{ "name": "x.exe", "size": {{size}}, "browser_download_url": "{{url}}" }]""");

        Assert.Null(GitHubReleaseParser.Parse(json, Repo));
    }

    [Fact]
    public void Long_notes_are_cut_and_oversized_responses_rejected()
    {
        var longNotes = GitHubReleaseParser.Parse(Release(body: new string('x', GitHubReleaseParser.MaxNotesLength + 500)), Repo);
        Assert.Equal(GitHubReleaseParser.MaxNotesLength + 2, longNotes!.Notes.Length);

        Assert.Null(GitHubReleaseParser.Parse(new string(' ', GitHubReleaseParser.MaxResponseBytes + 1), Repo));
        Assert.Null(GitHubReleaseParser.Parse("{ not json", Repo));
    }

    internal static string Release((string Field, string Value)? replace = null, string? assets = null, string? body = null, string tag = "v1.3.0")
    {
        var fields = new Dictionary<string, string>
        {
            ["tag_name"] = $"\"{tag}\"",
            ["name"] = $"\"Dynamic Island {tag.TrimStart('v')}\"",
            ["draft"] = "false",
            ["prerelease"] = "false",
            ["published_at"] = "\"2026-11-02T10:00:00Z\"",
            ["html_url"] = $"\"https://github.com/{Repo}/releases/tag/{tag}\"",
            ["body"] = System.Text.Json.JsonSerializer.Serialize(body ?? "## Features\r\n- <script>alert(1)</script> stays text"),
            ["assets"] = assets ?? $$"""[{ "name": "DynamicIsland-Setup-win-x64.exe", "size": 51000000, "browser_download_url": "https://github.com/{{Repo}}/releases/download/{{tag}}/DynamicIsland-Setup-win-x64.exe" }]""",
        };
        if (replace is { } r)
        {
            fields[r.Field] = r.Value;
        }

        return "{ " + string.Join(", ", fields.Select(f => $"\"{f.Key}\": {f.Value}")) + " }";
    }
}
