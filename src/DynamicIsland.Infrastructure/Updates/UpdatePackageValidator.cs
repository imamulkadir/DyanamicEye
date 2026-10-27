using System.IO.Compression;
using DynamicIsland.Core.Distribution;
using DynamicIsland.Core.Updates;

namespace DynamicIsland.Infrastructure.Updates;

/// <summary>
/// Checks a downloaded, hash-verified package before it may be installed (guide P06-05, ADR 0034): the setup program
/// must be a Windows executable; the portable ZIP must hold exactly the release layout of the expected version (ADR 0030)
/// with no path that could escape the folder it is extracted to. Never runs or extracts anything.
/// </summary>
public static class UpdatePackageValidator
{
    public const int MaxZipEntries = 20_000;
    public const long MaxZipBytes = 2L * 1024 * 1024 * 1024;

    /// <returns><c>null</c> when valid, otherwise a short reason code for the log.</returns>
    public static string? Validate(string path, ReleaseAsset asset, SemanticVersion version)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(version);
        if (string.Equals(asset.Name, UpdatePackages.SetupAsset, StringComparison.OrdinalIgnoreCase))
        {
            return IsWindowsExecutable(path) ? null : "not-an-executable";
        }

        if (string.Equals(asset.Name, UpdatePackages.PortableAsset, StringComparison.OrdinalIgnoreCase))
        {
            return ValidateZip(path, version);
        }

        return "unknown-package";
    }

    private static bool IsWindowsExecutable(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 0x40 || reader.ReadUInt16() != 0x5A4D) // "MZ"
        {
            return false;
        }

        stream.Position = 0x3C;
        var peOffset = reader.ReadInt32();
        if (peOffset <= 0 || peOffset > stream.Length - 4)
        {
            return false;
        }

        stream.Position = peOffset;
        return reader.ReadUInt32() == 0x00004550; // "PE\0\0"
    }

    private static string? ValidateZip(string path, SemanticVersion version)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            if (zip.Entries.Count is 0 or > MaxZipEntries)
            {
                return "zip-entry-count";
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            ZipArchiveEntry? pointer = null;
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (name.Length == 0 || name.StartsWith('/') || name.Contains(':', StringComparison.Ordinal)
                    || name.Split('/').Any(part => part is ".." or "."))
                {
                    return "zip-unsafe-path";
                }

                total += entry.Length;
                if (total > MaxZipBytes)
                {
                    return "zip-too-large";
                }

                names.Add(name);
                if (string.Equals(name, AppLayout.PointerFileName, StringComparison.OrdinalIgnoreCase))
                {
                    pointer = entry;
                }
            }

            if (!names.Contains(AppLayout.RootLauncher) || !names.Contains($"{AppLayout.PayloadPrefix}{version}/{AppLayout.AppExecutable}") || pointer is null
                || pointer.Length > AppLayout.MaxPointerBytes)
            {
                return "zip-not-a-release-layout";
            }

            using var reader = new StreamReader(pointer.Open());
            return AppLayout.ParsePointer(reader.ReadToEnd()) is { } layout && layout.Current == version.ToString() ? null : "zip-wrong-version";
        }
        catch (InvalidDataException)
        {
            return "zip-damaged";
        }
    }
}
