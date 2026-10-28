using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using DynamicIsland.Core.Updates;
using DynamicIsland.Infrastructure.Updates;

namespace DynamicIsland.UnitTests.Updates;

/// <summary>Verified downloads (P06-05, ADR 0034) with a test signing key and a fake GitHub — no network.</summary>
public sealed class GitHubUpdateDownloaderTests : IDisposable
{
    private const string Base = "https://github.com/x/y/releases/download/v1.3.0/";
    private static readonly SemanticVersion Version = new(1, 3, 0, null);

    private readonly string _staging = Path.Combine(Path.GetTempPath(), "DynamicIsland.UnitTests", Guid.NewGuid().ToString("N"), "updates");
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.Ordinal);
    private readonly List<string> _requested = [];

    public GitHubUpdateDownloaderTests()
    {
        PublicKey = Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string PublicKey { get; }

    public void Dispose()
    {
        _key.Dispose();
        var root = Path.GetDirectoryName(_staging)!;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_signed_setup_program_is_downloaded_verified_and_staged()
    {
        var setup = FakeExecutable();
        var release = Publish((UpdatePackages.SetupAsset, setup));
        var progress = new List<long>();

        var result = await Downloader().DownloadAsync(release, Asset(release, UpdatePackages.SetupAsset), new SyncProgress(progress.Add), Ct);

        Assert.Null(result.Problem);
        Assert.Equal(Path.Combine(_staging, "1.3.0", UpdatePackages.SetupAsset), result.Package!.Path);
        Assert.Equal(setup, File.ReadAllBytes(result.Package.Path));
        Assert.Equal(setup.Length, progress[^1]);
        Assert.Single(Directory.GetFiles(Path.Combine(_staging, "1.3.0"))); // No .partial left.
    }

    [Fact]
    public async Task A_signed_portable_zip_with_the_release_layout_is_accepted()
    {
        var release = Publish((UpdatePackages.PortableAsset, Zip("1.3.0")));

        var result = await Downloader().DownloadAsync(release, Asset(release, UpdatePackages.PortableAsset), new SyncProgress(_ => { }), Ct);

        Assert.Null(result.Problem);
        Assert.True(File.Exists(result.Package!.Path));
    }

    [Fact]
    public async Task Without_a_pinned_key_or_signature_nothing_is_downloaded()
    {
        var release = Publish((UpdatePackages.SetupAsset, FakeExecutable()));
        var unsigned = release with { Assets = [.. release.Assets.Where(a => a.Name != ReleaseManifest.SignatureFileName)] };

        var noKey = await Downloader(keys: []).DownloadAsync(release, Asset(release, UpdatePackages.SetupAsset), new SyncProgress(_ => { }), Ct);
        var noSignature = await Downloader().DownloadAsync(unsigned, Asset(release, UpdatePackages.SetupAsset), new SyncProgress(_ => { }), Ct);

        Assert.Equal(GitHubUpdateDownloader.Unverifiable, noKey.Problem);
        Assert.Equal(GitHubUpdateDownloader.Unverifiable, noSignature.Problem);
        Assert.DoesNotContain(_requested, u => u.EndsWith(UpdatePackages.SetupAsset, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_manifest_signed_by_another_key_or_changed_after_signing_is_refused()
    {
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var release = Publish((UpdatePackages.SetupAsset, FakeExecutable()), signer: attacker);
        var downloader = Downloader();

        Assert.Equal(GitHubUpdateDownloader.Unverifiable,
            (await downloader.DownloadAsync(release, Asset(release, UpdatePackages.SetupAsset), new SyncProgress(_ => { }), Ct)).Problem);

        Publish((UpdatePackages.SetupAsset, FakeExecutable()));
        var signed = await _routes[Base + ReleaseManifest.FileName]().Content.ReadAsStringAsync(Ct);
        _routes[Base + ReleaseManifest.FileName] = () => Ok(Encoding.UTF8.GetBytes(signed.Replace("\"size\":128", "\"size\":129", StringComparison.Ordinal)));
        Assert.Equal(GitHubUpdateDownloader.Unverifiable,
            (await downloader.DownloadAsync(release, Asset(release, UpdatePackages.SetupAsset), new SyncProgress(_ => { }), Ct)).Problem);
        Assert.Equal("unsigned", downloader.LastFailure);
        Assert.DoesNotContain(_requested, u => u.EndsWith(UpdatePackages.SetupAsset, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_manifest_for_another_version_or_size_is_refused()
    {
        var older = Publish((UpdatePackages.SetupAsset, FakeExecutable()), manifestVersion: "1.2.0");
        var downloader = Downloader();
        Assert.Equal(GitHubUpdateDownloader.Unverifiable,
            (await downloader.DownloadAsync(older, Asset(older, UpdatePackages.SetupAsset), new SyncProgress(_ => { }), Ct)).Problem);

        var release = Publish((UpdatePackages.SetupAsset, FakeExecutable()));
        var resized = Asset(release, UpdatePackages.SetupAsset) with { SizeBytes = 999 };
        Assert.Equal(GitHubUpdateDownloader.Unverifiable, (await downloader.DownloadAsync(release, resized, new SyncProgress(_ => { }), Ct)).Problem);
        Assert.Equal("not-in-manifest", downloader.LastFailure);
    }

    [Theory]
    [InlineData("same-length")]
    [InlineData("longer")]
    [InlineData("shorter")]
    public async Task A_file_that_does_not_match_the_signed_hash_is_deleted(string swap)
    {
        var setup = FakeExecutable();
        var release = Publish((UpdatePackages.SetupAsset, setup));
        var served = swap switch
        {
            "same-length" => [.. setup[..^1], (byte)(setup[^1] ^ 0xFF)],
            "longer" => [.. setup, 0],
            _ => setup[..^1],
        };
        _routes[Base + UpdatePackages.SetupAsset] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(served)) };

        var result = await Downloader().DownloadAsync(release, Asset(release, UpdatePackages.SetupAsset), new SyncProgress(_ => { }), Ct);

        Assert.Equal(GitHubUpdateDownloader.Mismatch, result.Problem);
        Assert.Empty(Directory.GetFiles(Path.Combine(_staging, "1.3.0")));
    }

    [Theory]
    [InlineData("zip-slip")]
    [InlineData("wrong-version")]
    [InlineData("not-a-layout")]
    public async Task A_correctly_signed_but_unsafe_or_wrong_zip_is_rejected(string problem)
    {
        var zip = problem switch
        {
            "zip-slip" => Zip("1.3.0", extra: "../evil.exe"),
            "wrong-version" => Zip("1.2.0"),
            _ => Zip("1.3.0", launcher: false),
        };
        var release = Publish((UpdatePackages.PortableAsset, zip));

        var result = await Downloader().DownloadAsync(release, Asset(release, UpdatePackages.PortableAsset), new SyncProgress(_ => { }), Ct);

        Assert.Equal(GitHubUpdateDownloader.Mismatch, result.Problem);
        Assert.Empty(Directory.GetFiles(Path.Combine(_staging, "1.3.0")));
    }

    [Fact]
    public async Task A_setup_file_that_is_not_a_windows_program_is_rejected()
    {
        var release = Publish((UpdatePackages.SetupAsset, Encoding.ASCII.GetBytes(new string('x', 200))));

        var result = await Downloader().DownloadAsync(release, Asset(release, UpdatePackages.SetupAsset), new SyncProgress(_ => { }), Ct);

        Assert.Equal(GitHubUpdateDownloader.Mismatch, result.Problem);
    }

    [Fact]
    public async Task Network_failures_and_stalls_are_interruptions()
    {
        var release = Publish((UpdatePackages.SetupAsset, FakeExecutable()));
        _routes[Base + UpdatePackages.SetupAsset] = () => throw new HttpRequestException("reset");
        var downloader = Downloader();
        Assert.Equal(GitHubUpdateDownloader.Interrupted,
            (await downloader.DownloadAsync(release, Asset(release, UpdatePackages.SetupAsset), new SyncProgress(_ => { }), Ct)).Problem);

        _routes[Base + UpdatePackages.SetupAsset] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) };
        Assert.Equal(GitHubUpdateDownloader.Interrupted,
            (await downloader.DownloadAsync(release, Asset(release, UpdatePackages.SetupAsset), new SyncProgress(_ => { }), Ct)).Problem);
        Assert.Equal("stalled", downloader.LastFailure);
        Assert.Empty(Directory.GetFiles(Path.Combine(_staging, "1.3.0")));
    }

    [Fact]
    public async Task Cancel_stops_the_download_and_leaves_nothing()
    {
        var release = Publish((UpdatePackages.SetupAsset, FakeExecutable()));
        _routes[Base + UpdatePackages.SetupAsset] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) };
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Downloader(stall: TimeSpan.FromMinutes(1)).DownloadAsync(release, Asset(release, UpdatePackages.SetupAsset), new SyncProgress(_ => { }), cancel.Token));

        Assert.Empty(Directory.GetFiles(Path.Combine(_staging, "1.3.0")));
    }

    [Fact]
    public async Task Not_enough_free_space_stops_before_downloading()
    {
        var release = Publish((UpdatePackages.PortableAsset, Zip("1.3.0")));
        var downloader = new GitHubUpdateDownloader(new HttpClient(new RouteHandler(this)), _staging, [PublicKey], TimeSpan.FromMilliseconds(300),
            freeBytes: _ => GitHubUpdateDownloader.SpareBytes);

        var result = await downloader.DownloadAsync(release, Asset(release, UpdatePackages.PortableAsset), new SyncProgress(_ => { }), Ct);

        Assert.Equal(GitHubUpdateDownloader.NotEnoughSpace, result.Problem);
        Assert.Equal("disk-space", downloader.LastFailure);
        Assert.DoesNotContain(_requested, u => u.EndsWith(UpdatePackages.PortableAsset, StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(Path.Combine(_staging, "1.3.0")));
    }

    [Fact]
    public async Task Only_the_version_being_downloaded_stays_in_staging()
    {
        Directory.CreateDirectory(Path.Combine(_staging, "1.2.0"));
        File.WriteAllText(Path.Combine(_staging, "1.2.0", "old.exe"), "old");
        var release = Publish((UpdatePackages.SetupAsset, FakeExecutable()));

        await Downloader().DownloadAsync(release, Asset(release, UpdatePackages.SetupAsset), new SyncProgress(_ => { }), Ct);

        Assert.Equal([Path.Combine(_staging, "1.3.0")], Directory.GetDirectories(_staging));
    }

    private GitHubUpdateDownloader Downloader(IReadOnlyList<string>? keys = null, TimeSpan? stall = null) =>
        new(new HttpClient(new RouteHandler(this)), _staging, keys ?? [PublicKey], stall ?? TimeSpan.FromMilliseconds(300));

    /// <summary>Serves the files and a manifest signed by <paramref name="signer"/> (the pinned test key by default).</summary>
    private ReleaseInfo Publish((string Name, byte[] Content) file, ECDsa? signer = null, string manifestVersion = "1.3.0")
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(file.Content));
        var manifest = Encoding.UTF8.GetBytes(
            $$"""{"schemaVersion":1,"product":"DynamicIsland","version":"{{manifestVersion}}","files":[{"name":"{{file.Name}}","size":{{file.Content.Length}},"sha256":"{{hash}}"}]}""");
        var signature = Convert.ToBase64String((signer ?? _key).SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

        _routes[Base + file.Name] = () => Ok(file.Content);
        _routes[Base + ReleaseManifest.FileName] = () => Ok(manifest);
        _routes[Base + ReleaseManifest.SignatureFileName] = () => Ok(Encoding.ASCII.GetBytes(signature));
        return new ReleaseInfo(Version, "v1.3.0", "Dynamic Island 1.3.0", DateTimeOffset.UnixEpoch, new Uri("https://github.com/x/y/releases/tag/v1.3.0"), string.Empty,
        [
            new ReleaseAsset(file.Name, file.Content.Length, new Uri(Base + file.Name)),
            new ReleaseAsset(ReleaseManifest.FileName, manifest.Length, new Uri(Base + ReleaseManifest.FileName)),
            new ReleaseAsset(ReleaseManifest.SignatureFileName, signature.Length, new Uri(Base + ReleaseManifest.SignatureFileName)),
        ]);
    }

    private static ReleaseAsset Asset(ReleaseInfo release, string name) => release.Assets.Single(a => a.Name == name);

    private static HttpResponseMessage Ok(byte[] content) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

    /// <summary>"MZ" header pointing at a "PE\0\0" signature.</summary>
    private static byte[] FakeExecutable()
    {
        var bytes = new byte[128];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BitConverter.GetBytes(0x40).CopyTo(bytes, 0x3C);
        "PE\0\0"u8.CopyTo(bytes.AsSpan(0x40));
        return bytes;
    }

    private static byte[] Zip(string pointerVersion, string? extra = null, bool launcher = true)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string text)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(text);
            }

            if (launcher)
            {
                Add("DynamicIsland.exe", "launcher");
            }

            Add("current.json", $$"""{ "current": "{{pointerVersion}}", "previous": null }""");
            Add($"app-{pointerVersion}/DynamicIsland.exe", "app");
            if (extra is not null)
            {
                Add(extra, "x");
            }
        }

        return buffer.ToArray();
    }

    private sealed class RouteHandler(GitHubUpdateDownloaderTests test) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            test._requested.Add(url);
            return Task.FromResult(test._routes.TryGetValue(url, out var respond) ? respond() : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class SyncProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }

    /// <summary>Sends nothing until cancelled.</summary>
    private sealed class StallingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
