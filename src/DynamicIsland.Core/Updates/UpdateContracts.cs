namespace DynamicIsland.Core.Updates;

/// <summary>A downloadable file of a release. Sizes come from GitHub's metadata, never guessed (guide §7).</summary>
public sealed record ReleaseAsset(string Name, long SizeBytes, Uri DownloadUrl);

/// <summary>
/// A published release (ADR 0032). <see cref="Notes"/> is the release body as plain text: untrusted, shown as text only,
/// never as HTML or anything executable (guide P06-05).
/// </summary>
public sealed record ReleaseInfo(
    SemanticVersion Version,
    string Tag,
    string Title,
    DateTimeOffset PublishedUtc,
    Uri PageUrl,
    string Notes,
    IReadOnlyList<ReleaseAsset> Assets);

/// <summary>What one request to the release source produced.</summary>
public enum ReleaseFetchStatus
{
    /// <summary>A release was returned (<see cref="ReleaseFetch.Release"/>, <see cref="ReleaseFetch.ETag"/>).</summary>
    Found,

    /// <summary>Unchanged since the cached <c>ETag</c> (HTTP 304): not counted against the API limit.</summary>
    NotModified,

    /// <summary>The project has no published release yet (HTTP 404).</summary>
    NoRelease,

    /// <summary>GitHub's rate limit; retry after <see cref="ReleaseFetch.RetryAfterUtc"/>.</summary>
    RateLimited,

    /// <summary>No network, DNS failure, timeout or server error.</summary>
    Unreachable,

    /// <summary>The response could not be trusted (bad JSON, unexpected URLs, oversized, not SemVer).</summary>
    Invalid,
}

public sealed record ReleaseFetch(ReleaseFetchStatus Status, ReleaseInfo? Release = null, string? ETag = null, DateTimeOffset? RetryAfterUtc = null);

/// <summary>Where releases come from (GitHub <c>releases/latest</c> in production; fakes in tests).</summary>
public interface IReleaseSource
{
    /// <param name="etag">The cached <c>ETag</c>, for a conditional request; <c>null</c> for a full one.</param>
    Task<ReleaseFetch> FetchLatestAsync(string? etag, CancellationToken cancellationToken);
}

public enum UpdateCheckOutcome
{
    UpToDate,
    UpdateAvailable,

    /// <summary>The project has not published a release yet.</summary>
    NoReleases,

    /// <summary>Not checked: self-update is managed by IT (ADR 0031).</summary>
    DisabledByPolicy,

    /// <summary>Not checked: the user has not allowed automatic checks.</summary>
    NotAllowed,

    /// <summary>Not checked: the last check is recent enough (automatic) or a manual check ran moments ago.</summary>
    NotDue,

    /// <summary>Not checked or failed: GitHub's rate limit is in effect.</summary>
    RateLimited,
    Failed,
}

/// <param name="Latest">The newest release known (also from the cache), when there is one.</param>
/// <param name="CheckedUtc">When GitHub last answered successfully, if ever.</param>
public sealed record UpdateCheckResult(UpdateCheckOutcome Outcome, ReleaseInfo? Latest = null, DateTimeOffset? CheckedUtc = null);

/// <summary>Guide §3 contract: decides whether to ask GitHub and compares the answer with the installed version.</summary>
public interface IUpdateChecker
{
    /// <summary>The result of the last check (also loaded from the cache at start).</summary>
    UpdateCheckResult? Last { get; }

    /// <param name="manual">"Check now": skips the daily interval and the consent (the user asked), but not the IT policy or rate limits.</param>
    Task<UpdateCheckResult> CheckAsync(bool manual, CancellationToken cancellationToken);
}

/// <summary>Persisted between runs: when to check next, the ETag and the last answer.</summary>
public sealed record UpdateCheckState(
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastSuccessUtc,
    int ConsecutiveFailures,
    DateTimeOffset? RetryAfterUtc,
    string? ETag,
    ReleaseInfo? Latest,
    bool NoReleases)
{
    public static UpdateCheckState Empty { get; } = new(null, null, 0, null, null, null, false);

    /// <summary>The newest version the tray has already announced, so each version is announced once (ADR 0033).</summary>
    public string? NotifiedVersion { get; init; }
}

public interface IUpdateStateStore
{
    UpdateCheckState Load();

    void Save(UpdateCheckState state);
}
