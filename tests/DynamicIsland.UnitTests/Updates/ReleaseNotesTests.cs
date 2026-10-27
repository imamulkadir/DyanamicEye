using DynamicIsland.Core.Updates;

namespace DynamicIsland.UnitTests.Updates;

/// <summary>Release notes as plain-text sections (P06-04, ADR 0033): untrusted Markdown, shown as text only.</summary>
public sealed class ReleaseNotesTests
{
    [Fact]
    public void Headings_become_sections_and_list_items_lines()
    {
        var sections = ReleaseNotes.Parse(
            "Thanks for using Dynamic Island.\n\n## Features\n- **Media** widget shows `album art`\n* Pin the island\n\n### Fixes\n1. Scroll bar flash\n2) Stats tab\n   wrapped onto a second line\n");

        Assert.Equal(
            ["|Thanks for using Dynamic Island.", "Features|Media widget shows album art|Pin the island", "Fixes|Scroll bar flash|Stats tab wrapped onto a second line"],
            sections.Select(s => string.Join('|', s.Items.Prepend(s.Heading))));
    }

    [Fact]
    public void Links_keep_their_words_and_never_their_address()
    {
        var item = ReleaseNotes.Parse("- See [the docs](https://evil.example/x) ![logo](https://evil.example/a.png)here").Single().Items.Single();

        Assert.Equal("See the docs here", item);
        Assert.DoesNotContain("evil.example", item, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_and_scripts_stay_inert_text()
    {
        var item = ReleaseNotes.Parse("- <script>alert(1)</script> stays text").Single().Items.Single();

        Assert.Equal("<script>alert(1)</script> stays text", item);
    }

    [Fact]
    public void Empty_notes_and_rules_produce_nothing_and_snake_case_survives()
    {
        Assert.Empty(ReleaseNotes.Parse(null));
        Assert.Empty(ReleaseNotes.Parse("## Features\n\n---\n"));
        Assert.Equal("Reads update_check.json", ReleaseNotes.Parse("- Reads update_check.json").Single().Items.Single());
    }

    [Fact]
    public void Huge_notes_are_bounded()
    {
        var many = string.Join('\n', Enumerable.Range(0, 100).Select(i => $"## S{i}\n" + string.Join('\n', Enumerable.Range(0, 100).Select(j => "- " + new string('x', 900)))));

        var sections = ReleaseNotes.Parse(many);

        Assert.Equal(ReleaseNotes.MaxSections, sections.Count);
        Assert.All(sections, s => Assert.Equal(ReleaseNotes.MaxItemsPerSection, s.Items.Count));
        Assert.All(sections.SelectMany(s => s.Items), i => Assert.Equal(ReleaseNotes.MaxItemLength + 1, i.Length));
    }
}
