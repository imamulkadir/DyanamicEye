using DynamicIsland.Infrastructure.Lifecycle;
using DynamicIsland.UnitTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynamicIsland.UnitTests.Lifecycle;

public sealed class FileSessionStateMarkerTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void First_session_is_not_reported_as_unclean()
    {
        var marker = CreateMarker();

        Assert.False(marker.BeginSession());
        Assert.True(File.Exists(_directory.Combine(FileSessionStateMarker.FileName)));
    }

    [Fact]
    public void Clean_exit_clears_the_marker()
    {
        CreateMarker().BeginSession();
        CreateMarker().EndSessionCleanly();

        Assert.False(CreateMarker().BeginSession());
    }

    [Fact]
    public void Session_that_never_ended_cleanly_is_reported_next_time()
    {
        CreateMarker().BeginSession();

        Assert.True(CreateMarker().BeginSession());
    }

    [Fact]
    public void Unwritable_location_does_not_throw()
    {
        var blocker = _directory.Combine("blocked");
        File.WriteAllText(blocker, "file in place of folder");
        var marker = new FileSessionStateMarker(Path.Combine(blocker, "data"), NullLogger<FileSessionStateMarker>.Instance);

        Assert.False(marker.BeginSession());
        marker.EndSessionCleanly();
    }

    private FileSessionStateMarker CreateMarker() => new(_directory.Path, NullLogger<FileSessionStateMarker>.Instance);
}
