using System.Text;
using System.Text.RegularExpressions;

namespace DynamicIsland.Core.Updates;

/// <param name="Heading">"Features", "Fixes", …; empty for text before the first heading.</param>
public sealed record ReleaseNoteSection(string Heading, IReadOnlyList<string> Items);

/// <summary>
/// Turns a release body (GitHub Markdown, untrusted) into plain-text sections for the update window (guide P06-04,
/// ADR 0033): headings become section titles, list items and paragraphs become lines, inline Markdown is reduced to its
/// text (links keep their words, never their address). Nothing is interpreted as HTML or made clickable.
/// </summary>
public static partial class ReleaseNotes
{
    public const int MaxSections = 12;
    public const int MaxItemsPerSection = 40;
    public const int MaxItemLength = 500;

    public static IReadOnlyList<ReleaseNoteSection> Parse(string? notes)
    {
        var sections = new List<ReleaseNoteSection>();
        var heading = string.Empty;
        var items = new List<string>();
        var paragraph = new StringBuilder();

        void EndParagraph()
        {
            if (paragraph.Length > 0)
            {
                Add(items, paragraph.ToString());
                paragraph.Clear();
            }
        }

        void EndSection()
        {
            EndParagraph();
            if (items.Count > 0 && sections.Count < MaxSections)
            {
                sections.Add(new ReleaseNoteSection(heading, [.. items]));
            }

            items.Clear();
        }

        foreach (var raw in (notes ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (HeadingPattern().Match(line) is { Success: true } h)
            {
                EndSection();
                heading = Inline(h.Groups[1].Value);
            }
            else if (ItemPattern().Match(line) is { Success: true } item)
            {
                EndParagraph();
                paragraph.Append(item.Groups[1].Value);
            }
            else if (line.Length == 0 || line.StartsWith("```", StringComparison.Ordinal) || RulePattern().IsMatch(line))
            {
                EndParagraph();
            }
            else
            {
                // A wrapped continuation of the item or paragraph above.
                paragraph.Append(paragraph.Length > 0 ? " " : string.Empty).Append(line.TrimStart('>', ' '));
            }
        }

        EndSection();
        return sections;
    }

    private static void Add(List<string> items, string text)
    {
        var plain = Inline(text);
        if (plain.Length > 0 && items.Count < MaxItemsPerSection)
        {
            items.Add(plain.Length > MaxItemLength ? plain[..MaxItemLength] + "…" : plain);
        }
    }

    /// <summary>Reduces inline Markdown to its words: <c>[text](url)</c> → text, images dropped, emphasis and code marks removed.</summary>
    private static string Inline(string text)
    {
        text = ImagePattern().Replace(text, string.Empty);
        text = LinkPattern().Replace(text, "$1");
        text = EmphasisPattern().Replace(text, string.Empty);
        text = SpacePattern().Replace(text, " ");
        return text.Trim();
    }

    [GeneratedRegex(@"^#{1,6}\s+(.+?)\s*#*$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^(?:[-*+]|\d{1,3}[.)])\s+(?:\[[ xX]\]\s+)?(.*)$")]
    private static partial Regex ItemPattern();

    [GeneratedRegex(@"^(?:-{3,}|\*{3,}|_{3,})$")]
    private static partial Regex RulePattern();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex ImagePattern();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"\*\*|__|(?<!\w)[*_](?=\S)|(?<=\S)[*_](?!\w)|`")]
    private static partial Regex EmphasisPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacePattern();
}
