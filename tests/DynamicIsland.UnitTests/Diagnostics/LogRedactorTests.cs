using DynamicIsland.Core.Diagnostics;

namespace DynamicIsland.UnitTests.Diagnostics;

public sealed class LogRedactorTests
{
    private static readonly LogRedactor Redactor = new(
    [
        (@"C:\Users\alice", "%USERPROFILE%"),
        ("C:/Users/alice", "%USERPROFILE%"),
        ("alice", "[USER]"),
        ("DESKTOP-42", "[MACHINE]"),
    ]);

    [Theory]
    [InlineData(@"Loading C:\Users\alice\AppData\Local\x.json", @"Loading %USERPROFILE%\AppData\Local\x.json")]
    [InlineData(@"path c:\users\ALICE\file", @"path %USERPROFILE%\file")]
    [InlineData("C:/Users/alice/notes", "%USERPROFILE%/notes")]
    [InlineData("user alice signed in", "user [USER] signed in")]
    [InlineData("host DESKTOP-42 ready", "host [MACHINE] ready")]
    public void Environment_literals_are_replaced(string input, string expected)
    {
        Assert.Equal(expected, Redactor.Redact(input));
    }

    [Fact]
    public void User_name_inside_a_longer_word_is_kept()
    {
        Assert.Equal("malice and alicea stay", Redactor.Redact("malice and alicea stay"));
    }

    [Theory]
    [InlineData("token ghp_0123456789abcdefghijABCDEFGHIJ0123 used")]
    [InlineData("github_pat_11ABCDEFG0123456789_abcdefghijklmnopqrstuvwxyz")]
    [InlineData("gho_abcdefghijklmnopqrstuvwxyz0123")]
    public void GitHub_tokens_are_redacted(string input)
    {
        var output = Redactor.Redact(input);

        Assert.Contains(LogRedactor.TokenPlaceholder, output, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdefghij", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.sig", "eyJhbGciOiJIUzI1NiJ9")]
    [InlineData("password=hunter2&user=x", "hunter2")]
    [InlineData("api_key: \"abc 123\"", "abc 123")]
    [InlineData("ApiKey=XYZ987", "XYZ987")]
    [InlineData("refresh_token='r-1-2-3'", "r-1-2-3")]
    public void Secrets_are_redacted(string input, string secret)
    {
        var output = Redactor.Redact(input);

        Assert.DoesNotContain(secret, output, StringComparison.Ordinal);
        Assert.Contains(LogRedactor.SecretPlaceholder, output, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_addresses_are_redacted()
    {
        Assert.Equal($"contact {LogRedactor.EmailPlaceholder} now", Redactor.Redact("contact first.last+tag@example.co.uk now"));
    }

    [Fact]
    public void Ordinary_text_is_unchanged()
    {
        const string text = "Settings loaded: outcome=Loaded corrections=2 (appearance.opacity)";

        Assert.Equal(text, Redactor.Redact(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_input_gives_empty_output(string? input)
    {
        Assert.Equal(string.Empty, Redactor.Redact(input));
    }

    [Fact]
    public void Literals_shorter_than_three_characters_are_ignored()
    {
        var redactor = new LogRedactor([("ab", "[USER]"), (null, "[X]"), ("  ", "[Y]")]);

        Assert.Equal("ab cd", redactor.Redact("ab cd"));
    }

    [Fact]
    public void Current_environment_redactor_hides_profile_and_user_name()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var redactor = LogRedactor.ForCurrentEnvironment();

        var output = redactor.Redact($@"{profile}\AppData\Local\DynamicIsland by {Environment.UserName}");

        Assert.DoesNotContain(profile, output, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("%USERPROFILE%", output, StringComparison.Ordinal);
    }
}
