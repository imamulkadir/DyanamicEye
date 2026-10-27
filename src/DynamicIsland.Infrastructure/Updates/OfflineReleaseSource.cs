using DynamicIsland.Core.Updates;

namespace DynamicIsland.Infrastructure.Updates;

/// <summary>Never touches the network: used by Debug builds running under the test data-root hook, so tests stay offline.</summary>
public sealed class OfflineReleaseSource : IReleaseSource
{
    public Task<ReleaseFetch> FetchLatestAsync(string? etag, CancellationToken cancellationToken) =>
        Task.FromResult(new ReleaseFetch(ReleaseFetchStatus.Unreachable));
}
