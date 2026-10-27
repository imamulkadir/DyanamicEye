using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using DynamicIsland.Core.Updates;

namespace DynamicIsland.Infrastructure.Updates;

/// <summary>
/// GitHub REST <c>releases/latest</c> for this project, unauthenticated (60 requests per hour per IP, ADR 0032). Sends a
/// descriptive User-Agent and the cached ETag (a 304 does not count against the limit), honors the rate-limit reset,
/// limits time and response size, and uses the system proxy. Never sends anything about the user or the PC.
/// </summary>
public sealed class GitHubReleaseSource(HttpClient http, string repository, string appVersion) : IReleaseSource
{
    /// <summary>The project's public repository (owner/name).</summary>
    public const string ProjectRepository = "imamulkadir/DyanamicEye";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public async Task<ReleaseFetch> FetchLatestAsync(string? etag, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://api.github.com/repos/{repository}/releases/latest"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("DynamicIsland", SafeVersion(appVersion)));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue($"(+https://github.com/{repository})"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrEmpty(etag) && EntityTagHeaderValue.TryParse(etag, out var tag))
        {
            request.Headers.IfNoneMatch.Add(tag);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            switch (response.StatusCode)
            {
                case HttpStatusCode.NotModified:
                    return new ReleaseFetch(ReleaseFetchStatus.NotModified, ETag: etag);
                case HttpStatusCode.NotFound:
                    return new ReleaseFetch(ReleaseFetchStatus.NoRelease);
                case HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests:
                    return RateLimit(response) is { } retryAfter
                        ? new ReleaseFetch(ReleaseFetchStatus.RateLimited, RetryAfterUtc: retryAfter)
                        : new ReleaseFetch(ReleaseFetchStatus.Unreachable);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new ReleaseFetch(ReleaseFetchStatus.Unreachable);
            }

            if (response.Content.Headers.ContentLength > GitHubReleaseParser.MaxResponseBytes)
            {
                return new ReleaseFetch(ReleaseFetchStatus.Invalid);
            }

            var json = await ReadBoundedAsync(response.Content, timeout.Token).ConfigureAwait(false);
            var release = json is null ? null : GitHubReleaseParser.Parse(json, repository);
            return release is null
                ? new ReleaseFetch(ReleaseFetchStatus.Invalid)
                : new ReleaseFetch(ReleaseFetchStatus.Found, release, response.Headers.ETag?.ToString());
        }
        catch (HttpRequestException)
        {
            return new ReleaseFetch(ReleaseFetchStatus.Unreachable);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ReleaseFetch(ReleaseFetchStatus.Unreachable); // Our timeout.
        }
    }

    /// <summary>Retry time from <c>Retry-After</c> or, when the limit is used up, <c>X-RateLimit-Reset</c>; else <c>null</c>.</summary>
    private static DateTimeOffset? RateLimit(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is { } retryAfter)
        {
            if (retryAfter.Delta is { } delta)
            {
                return DateTimeOffset.UtcNow + delta;
            }

            if (retryAfter.Date is { } date)
            {
                return date;
            }
        }

        if (Header(response, "X-RateLimit-Remaining") == "0"
            && long.TryParse(Header(response, "X-RateLimit-Reset"), NumberStyles.None, CultureInfo.InvariantCulture, out var reset))
        {
            return DateTimeOffset.FromUnixTimeSeconds(reset);
        }

        return response.StatusCode == HttpStatusCode.TooManyRequests ? DateTimeOffset.UtcNow.AddHours(1) : null;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static async Task<string?> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > GitHubReleaseParser.MaxResponseBytes)
            {
                return null;
            }
        }

        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>A User-Agent product version may not contain spaces or build metadata separators that break the header.</summary>
    private static string SafeVersion(string version)
    {
        var plain = version.Split('+')[0];
        return plain.Length is > 0 and <= 64 && plain.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-') ? plain : "0.0.0";
    }
}
