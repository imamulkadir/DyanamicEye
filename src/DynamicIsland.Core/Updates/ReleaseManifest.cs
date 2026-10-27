using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DynamicIsland.Core.Updates;

/// <param name="Sha256">Lowercase hex.</param>
public sealed record ManifestFile(string Name, long SizeBytes, string Sha256);

/// <summary>
/// The signed list of a release's files (ADR 0034): <c>DynamicIsland-release.json</c> plus its signature
/// <c>DynamicIsland-release.json.sig</c>, both release assets. The signature, made with the project's private key and
/// checked against the public key pinned in the app, is what proves a download comes from the project; the GitHub
/// release itself is not trusted for that.
/// <code>
/// {"schemaVersion":1,"product":"DynamicIsland","version":"1.3.0",
///  "files":[{"name":"DynamicIsland-Setup-win-x64.exe","size":51000000,"sha256":"…64 hex…"}]}
/// </code>
/// </summary>
public sealed partial record ReleaseManifest(SemanticVersion Version, IReadOnlyList<ManifestFile> Files)
{
    public const string FileName = "DynamicIsland-release.json";
    public const string SignatureFileName = FileName + ".sig";
    public const string Product = "DynamicIsland";
    public const int MaxBytes = 64 * 1024;
    public const int MaxSignatureBytes = 1024;
    private const int SchemaVersion = 1;
    private const int MaxFiles = 50;

    public ManifestFile? Find(string name) =>
        Files.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <returns><c>null</c> for anything that is not a well-formed manifest of this product.</returns>
    public static ReleaseManifest? Parse(ReadOnlySpan<byte> json)
    {
        if (json.Length is 0 or > MaxBytes)
        {
            return null;
        }

        try
        {
            var reader = new Utf8JsonReader(json);
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number || schema.GetInt32() != SchemaVersion
                || Text(root, "product") != Product
                || SemanticVersion.TryParse(Text(root, "version")) is not { } version
                || !root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array || files.GetArrayLength() is 0 or > MaxFiles)
            {
                return null;
            }

            var list = new List<ManifestFile>();
            foreach (var file in files.EnumerateArray())
            {
                if (file.ValueKind != JsonValueKind.Object
                    || Text(file, "name") is not { } name || !NamePattern().IsMatch(name)
                    || !file.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out var bytes)
                    || bytes <= 0 || bytes > GitHubReleaseParser.MaxAssetBytes
                    || Text(file, "sha256") is not { } hash || !HashPattern().IsMatch(hash)
                    || list.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return null;
                }

                list.Add(new ManifestFile(name, bytes, hash));
            }

            return new ReleaseManifest(version, list);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Checks <paramref name="signature"/> (Base64 of an ECDSA P-256 / SHA-256 signature, IEEE P1363 r‖s) over the exact
    /// manifest bytes against any of the pinned public keys (Base64 SubjectPublicKeyInfo; several allow key rotation).
    /// </summary>
    public static bool IsSignedBy(ReadOnlySpan<byte> manifest, string? signature, IReadOnlyList<string> publicKeys)
    {
        ArgumentNullException.ThrowIfNull(publicKeys);
        if (manifest.Length is 0 or > MaxBytes || string.IsNullOrWhiteSpace(signature) || signature.Length > MaxSignatureBytes)
        {
            return false;
        }

        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(signature.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        foreach (var key in publicKeys)
        {
            using var ecdsa = ECDsa.Create();
            try
            {
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key), out _);
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException)
            {
                continue;
            }

            if (ecdsa.KeySize == 256 && ecdsa.VerifyData(manifest, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")]
    private static partial Regex NamePattern();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex HashPattern();
}

/// <summary>
/// The project's release-signing public keys (ADR 0034), pinned in the app. Empty until the owner generates the key
/// pair (<c>tools/New-ReleaseSigningKey.ps1</c>); while empty, no download can be verified, so the update window only
/// offers the release page.
/// </summary>
public static class ReleaseSigningKeys
{
    public static IReadOnlyList<string> Pinned { get; } = [];
}
