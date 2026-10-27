using System.Security.Cryptography;
using DynamicIsland.Core.Updates;

namespace DynamicIsland.Infrastructure.Updates;

/// <summary>
/// Downloads one release file into a staging folder and proves it is the project's (guide P06-05, ADR 0034):
/// <list type="number">
/// <item>fetch the release's signed manifest and check its signature against the pinned public key — without a valid
/// signature nothing is downloaded and the user is sent to the release page;</item>
/// <item>the manifest must name this release's version and this file with GitHub's size;</item>
/// <item>download with progress, cancel and a stall timeout, never more bytes than signed, hashing as it goes;</item>
/// <item>SHA-256 must match the signed value, then the content is checked (<see cref="UpdatePackageValidator"/>).</item>
/// </list>
/// Anything that fails is deleted. Nothing is ever run or extracted here. Only one version is kept in staging.
/// </summary>
/// <param name="publicKeys">The pinned keys (<see cref="ReleaseSigningKeys.Pinned"/>; test keys in tests).</param>
/// <param name="stallTimeout">No bytes for this long counts as an interrupted download (default <see cref="DefaultStallTimeout"/>).</param>
public sealed class GitHubUpdateDownloader(HttpClient http, string stagingRoot, IReadOnlyList<string> publicKeys, TimeSpan? stallTimeout = null) : IUpdateDownloader
{
    public static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _stallTimeout = stallTimeout ?? DefaultStallTimeout;

    /// <summary>Free space kept on top of the package (the installer or extraction needs room too).</summary>
    public const long SpareBytes = 200L * 1024 * 1024;

    public const string Unverifiable = "This release can't be confirmed as coming from the Dynamic Island project, so it won't be installed from here. Get it from the release page instead.";
    public const string Interrupted = "The download was interrupted. Check your internet connection and try again.";
    public const string Mismatch = "The downloaded file doesn't match the signed release, so it was deleted. Try again, or get it from the release page.";
    public const string NotEnoughSpace = "There isn't enough free disk space for the update. Free up some space and try again.";
    public const string NotSaved = "The update couldn't be saved on this PC. Try again later.";

    private const int BufferSize = 81920;

    /// <summary>Why the last download failed, as a short code for the log (never a path or URL).</summary>
    public string? LastFailure { get; private set; }

    public async Task<UpdateDownloadResult> DownloadAsync(ReleaseInfo release, ReleaseAsset asset, IProgress<long> received, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(received);
        LastFailure = null;

        string? partial = null;
        try
        {
            var signed = await ReadManifestAsync(release, cancellationToken).ConfigureAwait(false);
            if (signed?.Find(asset.Name) is not { } file || file.SizeBytes != asset.SizeBytes)
            {
                return Fail(signed is null ? "unsigned" : "not-in-manifest", Unverifiable);
            }

            var folder = PrepareStaging(release.Version);
            if (FreeBytes(folder) is { } free && free < file.SizeBytes + SpareBytes)
            {
                return Fail("disk-space", NotEnoughSpace);
            }

            var target = Path.Combine(folder, file.Name);
            partial = target + ".partial";
            var hash = await DownloadToAsync(asset.DownloadUrl, partial, file.SizeBytes, received, cancellationToken).ConfigureAwait(false);
            if (hash is null || !string.Equals(Convert.ToHexStringLower(hash), file.Sha256, StringComparison.Ordinal))
            {
                return Fail("hash-mismatch", Mismatch);
            }

            if (UpdatePackageValidator.Validate(partial, asset, release.Version) is { } invalid)
            {
                return Fail(invalid, Mismatch);
            }

            File.Move(partial, target, overwrite: true);
            partial = null;
            return new UpdateDownloadResult(new StagedPackage(asset, target), null);
        }
        catch (HttpRequestException)
        {
            return Fail("network", Interrupted);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail("stalled", Interrupted);
        }
        catch (IOException ex) when (IsDiskFull(ex))
        {
            return Fail("disk-full", NotEnoughSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(ex.GetType().Name, NotSaved);
        }
        finally
        {
            if (partial is not null)
            {
                TryDelete(partial);
            }
        }
    }

    /// <returns>The manifest when its signature verifies against a pinned key and it names this release; else <c>null</c>.</returns>
    private async Task<ReleaseManifest?> ReadManifestAsync(ReleaseInfo release, CancellationToken cancellationToken)
    {
        var manifestAsset = release.Assets.FirstOrDefault(a => string.Equals(a.Name, ReleaseManifest.FileName, StringComparison.OrdinalIgnoreCase));
        var signatureAsset = release.Assets.FirstOrDefault(a => string.Equals(a.Name, ReleaseManifest.SignatureFileName, StringComparison.OrdinalIgnoreCase));
        if (publicKeys.Count == 0 || manifestAsset is null || signatureAsset is null)
        {
            return null;
        }

        var manifest = await ReadSmallAsync(manifestAsset.DownloadUrl, ReleaseManifest.MaxBytes, cancellationToken).ConfigureAwait(false);
        var signature = await ReadSmallAsync(signatureAsset.DownloadUrl, ReleaseManifest.MaxSignatureBytes, cancellationToken).ConfigureAwait(false);
        if (manifest is null || signature is null || !ReleaseManifest.IsSignedBy(manifest, System.Text.Encoding.ASCII.GetString(signature), publicKeys))
        {
            return null;
        }

        return ReleaseManifest.Parse(manifest) is { } parsed && parsed.Version == release.Version ? parsed : null;
    }

    private async Task<byte[]?> ReadSmallAsync(Uri url, int maxBytes, CancellationToken cancellationToken)
    {
        using var stall = Stall(cancellationToken);
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > maxBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
        var buffer = new byte[maxBytes + 1];
        var length = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(length), stall.Token).ConfigureAwait(false)) > 0)
        {
            length += read;
            if (length > maxBytes)
            {
                return null;
            }
        }

        return buffer[..length];
    }

    /// <returns>The SHA-256 of what was written, or <c>null</c> when the server sent a different amount than signed.</returns>
    private async Task<byte[]?> DownloadToAsync(Uri url, string path, long expected, IProgress<long> received, CancellationToken cancellationToken)
    {
        using var stall = Stall(cancellationToken);
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is { } length && length != expected)
        {
            return null;
        }

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var source = await response.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
        await using (var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
        {
            var buffer = new byte[BufferSize];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, stall.Token).ConfigureAwait(false)) > 0)
            {
                stall.CancelAfter(_stallTimeout);
                total += read;
                if (total > expected)
                {
                    return null;
                }

                sha.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), stall.Token).ConfigureAwait(false);
                received.Report(total);
            }

            if (total != expected)
            {
                return null;
            }
        }

        return sha.GetHashAndReset();
    }

    /// <summary><c>updates\&lt;version&gt;</c>; other versions' leftovers are removed.</summary>
    private string PrepareStaging(SemanticVersion version)
    {
        var folder = Path.Combine(stagingRoot, version.ToString());
        if (Directory.Exists(stagingRoot))
        {
            foreach (var old in Directory.EnumerateDirectories(stagingRoot).Where(d => !string.Equals(d, folder, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    Directory.Delete(old, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // In use or locked: removed next time.
                }
            }
        }

        Directory.CreateDirectory(folder);
        return folder;
    }

    private static long? FreeBytes(string folder)
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null; // Unknown: the write itself reports a full disk.
        }
    }

    private CancellationTokenSource Stall(CancellationToken cancellationToken)
    {
        var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stall.CancelAfter(_stallTimeout);
        return stall;
    }

    /// <summary>ERROR_HANDLE_DISK_FULL (39) or ERROR_DISK_FULL (112).</summary>
    private static bool IsDiskFull(IOException ex) => (ex.HResult & 0xFFFF) is 39 or 112;

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Removed with the staging folder next time.
        }
    }

    private UpdateDownloadResult Fail(string code, string problem)
    {
        LastFailure = code;
        return new UpdateDownloadResult(null, problem);
    }
}
