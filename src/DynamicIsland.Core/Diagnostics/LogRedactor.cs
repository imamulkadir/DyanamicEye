using System.Text.RegularExpressions;

namespace DynamicIsland.Core.Diagnostics;

/// <summary>
/// Scrubs personal and secret data from diagnostic text before it is written or shared (AGENTS.md, guide §4.5–4.6).
/// Redacts: the user profile path, user/machine/domain names (whole words only), GitHub tokens, bearer tokens,
/// <c>password=</c>/<c>token=</c>/<c>api_key=</c>-style assignments and email addresses.
/// It cannot recognise free text such as note contents or window titles: callers must never log those.
/// </summary>
public sealed partial class LogRedactor
{
    public const string TokenPlaceholder = "[REDACTED-TOKEN]";
    public const string SecretPlaceholder = "[REDACTED]";
    public const string EmailPlaceholder = "[REDACTED-EMAIL]";
    public const string FailedPlaceholder = "[REDACTION-FAILED: message withheld]";

    private const int MinLiteralLength = 3;
    private const int TimeoutMilliseconds = 200;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(TimeoutMilliseconds);

    private readonly (Regex Pattern, string Replacement)[] _literals;

    /// <param name="sensitiveLiterals">Exact values to hide and their placeholders. Values shorter than 3 characters are ignored.</param>
    public LogRedactor(IEnumerable<(string? Value, string Replacement)> sensitiveLiterals)
    {
        ArgumentNullException.ThrowIfNull(sensitiveLiterals);

        _literals = sensitiveLiterals
            .Where(l => !string.IsNullOrWhiteSpace(l.Value) && l.Value.Trim().Length >= MinLiteralLength)
            .Select(l => (Value: l.Value!.Trim(), l.Replacement))
            .DistinctBy(l => l.Value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(l => l.Value.Length)
            .Select(l => (LiteralPattern(l.Value), l.Replacement))
            .ToArray();
    }

    /// <summary>Redactor for the signed-in Windows user and this machine.</summary>
    public static LogRedactor ForCurrentEnvironment()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);
        return new LogRedactor(
        [
            (profile, "%USERPROFILE%"),
            (profile.Replace('\\', '/'), "%USERPROFILE%"),
            (Environment.UserName, "[USER]"),
            (Environment.MachineName, "[MACHINE]"),
            (Environment.UserDomainName, "[DOMAIN]"),
        ]);
    }

    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        try
        {
            var result = text;
            foreach (var (pattern, replacement) in _literals)
            {
                result = pattern.Replace(result, replacement);
            }

            result = GitHubTokenPattern().Replace(result, TokenPlaceholder);
            result = BearerPattern().Replace(result, "Bearer " + SecretPlaceholder);
            result = SecretAssignmentPattern().Replace(result, "${key}=" + SecretPlaceholder);
            result = EmailPattern().Replace(result, EmailPlaceholder);
            return result;
        }
        catch (RegexMatchTimeoutException)
        {
            // Fail closed: never emit text that could not be fully scrubbed.
            return FailedPlaceholder;
        }
    }

    /// <summary>Case-insensitive literal that is not part of a longer word (so user "ann" leaves "annual" alone).</summary>
    private static Regex LiteralPattern(string value) =>
        new($"(?<![A-Za-z0-9_]){Regex.Escape(value)}(?![A-Za-z0-9_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);

    [GeneratedRegex(@"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})\b", RegexOptions.CultureInvariant, TimeoutMilliseconds)]
    private static partial Regex GitHubTokenPattern();

    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeoutMilliseconds)]
    private static partial Regex BearerPattern();

    [GeneratedRegex(
        @"(?<key>\b(?:password|passwd|pwd|secret|client_secret|token|access_token|refresh_token|id_token|api[_-]?key|apikey)\b)\s*[:=]\s*(?:""[^""]*""|'[^']*'|[^\s&;,""']+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeoutMilliseconds)]
    private static partial Regex SecretAssignmentPattern();

    [GeneratedRegex(@"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b", RegexOptions.CultureInvariant, TimeoutMilliseconds)]
    private static partial Regex EmailPattern();
}
