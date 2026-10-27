using System.Net;
using System.Text;
using DynamicIsland.Core.Updates;
using DynamicIsland.Infrastructure.Updates;

namespace DynamicIsland.UnitTests.Updates;

/// <summary>The GitHub HTTP source and the cached state (P06-03, ADR 0032), with a fake handler — no network.</summary>
public sealed class GitHubReleaseSourceTests : IDisposable
{
    private const string Repo = "imamulkadir/DyanamicEye";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "DynamicIsland.UnitTests", Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task Request_identifies_the_app_sends_the_etag_and_parses_the_release()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SemanticVersionAndParserTests.Release(), Encoding.UTF8, "application/json") };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v2\"");
            return response;
        });
        using var http = new HttpClient(handler);

        var fetch = await new GitHubReleaseSource(http, Repo, "1.2.0+abc").FetchLatestAsync("\"v1\"", Ct);

        var request = handler.Requests.Single();
        Assert.Equal($"https://api.github.com/repos/{Repo}/releases/latest", request.RequestUri!.ToString());
        Assert.Equal($"DynamicIsland/1.2.0 (+https://github.com/{Repo})", request.Headers.UserAgent.ToString());
        Assert.Equal("\"v1\"", request.Headers.IfNoneMatch.Single().ToString());
        Assert.Equal("2022-11-28", request.Headers.GetValues("X-GitHub-Api-Version").Single());
        Assert.Null(request.Headers.Authorization); // Never a token or anything about the user.
        Assert.Equal(ReleaseFetchStatus.Found, fetch.Status);
        Assert.Equal("1.3.0", fetch.Release!.Version.ToString());
        Assert.Equal("\"v2\"", fetch.ETag);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotModified, ReleaseFetchStatus.NotModified)]
    [InlineData(HttpStatusCode.NotFound, ReleaseFetchStatus.NoRelease)]
    [InlineData(HttpStatusCode.InternalServerError, ReleaseFetchStatus.Unreachable)]
    [InlineData(HttpStatusCode.Forbidden, ReleaseFetchStatus.Unreachable)] // Forbidden without rate-limit headers.
    public async Task Status_codes_map_to_outcomes(HttpStatusCode code, ReleaseFetchStatus expected)
    {
        using var http = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(code)));

        Assert.Equal(expected, (await new GitHubReleaseSource(http, Repo, "1.2.0").FetchLatestAsync(null, Ct)).Status);
    }

    [Fact]
    public async Task Rate_limits_carry_the_reset_time()
    {
        using var exhausted = new HttpClient(new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
            response.Headers.Add("X-RateLimit-Remaining", "0");
            response.Headers.Add("X-RateLimit-Reset", "1793087224");
            return response;
        }));
        using var tooMany = new HttpClient(new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(10));
            return response;
        }));

        var first = await new GitHubReleaseSource(exhausted, Repo, "1.2.0").FetchLatestAsync(null, Ct);
        var second = await new GitHubReleaseSource(tooMany, Repo, "1.2.0").FetchLatestAsync(null, Ct);

        Assert.Equal((ReleaseFetchStatus.RateLimited, DateTimeOffset.FromUnixTimeSeconds(1793087224)), (first.Status, first.RetryAfterUtc!.Value));
        Assert.Equal(ReleaseFetchStatus.RateLimited, second.Status);
        Assert.InRange(second.RetryAfterUtc!.Value - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(11));
    }

    [Fact]
    public async Task Offline_untrusted_and_oversized_answers_never_throw()
    {
        using var offline = new HttpClient(new FakeHandler(_ => throw new HttpRequestException("no network")));
        using var garbage = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>captive portal</html>") }));
        using var huge = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string(' ', GitHubReleaseParser.MaxResponseBytes + 10)),
        }));

        Assert.Equal(ReleaseFetchStatus.Unreachable, (await new GitHubReleaseSource(offline, Repo, "1.2.0").FetchLatestAsync(null, Ct)).Status);
        Assert.Equal(ReleaseFetchStatus.Invalid, (await new GitHubReleaseSource(garbage, Repo, "1.2.0").FetchLatestAsync(null, Ct)).Status);
        Assert.Equal(ReleaseFetchStatus.Invalid, (await new GitHubReleaseSource(huge, Repo, "1.2.0").FetchLatestAsync(null, Ct)).Status);
    }

    [Fact]
    public async Task A_hanging_server_times_out_as_unreachable()
    {
        using var hanging = new HttpClient(new FakeHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var fetch = await new GitHubReleaseSource(hanging, Repo, "1.2.0").FetchLatestAsync(null, cancel.Token);

        Assert.Equal(ReleaseFetchStatus.Unreachable, fetch.Status);
        Assert.InRange(stopwatch.Elapsed, GitHubReleaseSource.Timeout - TimeSpan.FromSeconds(1), GitHubReleaseSource.Timeout + TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Cached_state_round_trips_and_bad_files_load_empty()
    {
        var store = new JsonUpdateStateStore(_folder);
        Assert.Equal(UpdateCheckState.Empty, store.Load());

        var release = GitHubReleaseParser.Parse(SemanticVersionAndParserTests.Release(), Repo)!;
        var state = new UpdateCheckState(DateTimeOffset.UnixEpoch.AddDays(20000), DateTimeOffset.UnixEpoch.AddDays(20000), 0, null, "\"e\"", release, false) { NotifiedVersion = "1.3.0" };
        store.Save(state);
        var loaded = store.Load();

        Assert.Equal(state with { Latest = null }, loaded with { Latest = null });
        Assert.Equal(release.Version, loaded.Latest!.Version);
        Assert.Equal(release.Assets.Single().DownloadUrl, loaded.Latest.Assets.Single().DownloadUrl);

        File.WriteAllText(Path.Combine(_folder, JsonUpdateStateStore.FileName), "{ broken");
        Assert.Equal(UpdateCheckState.Empty, store.Load());
    }

    [Fact]
    public void A_locally_edited_cache_with_a_foreign_link_is_not_trusted()
    {
        var store = new JsonUpdateStateStore(_folder);
        var release = GitHubReleaseParser.Parse(SemanticVersionAndParserTests.Release(), Repo)!;
        store.Save(UpdateCheckState.Empty with { Latest = release });
        var path = Path.Combine(_folder, JsonUpdateStateStore.FileName);
        File.WriteAllText(path, File.ReadAllText(path).Replace("https://github.com/", "https://evil.example/", StringComparison.Ordinal));

        Assert.Null(store.Load().Latest);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this((request, _) => Task.FromResult(respond(request)))
        {
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return respond(request, cancellationToken);
        }
    }
}
