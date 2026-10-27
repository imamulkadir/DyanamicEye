using System.Security.Cryptography;
using System.Text;
using DynamicIsland.Core.Updates;

namespace DynamicIsland.UnitTests.Updates;

/// <summary>The signed release manifest (P06-05, ADR 0034).</summary>
public sealed class ReleaseManifestTests
{
    // Produced by tools/New-ReleaseSigningKey.ps1 + tools/New-ReleaseManifest.ps1 (PowerShell 7.6) with a throwaway key
    // whose private half was deleted: proves the scripts and the app agree on bytes, curve and signature format.
    private const string ScriptPublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEgV4heFY4FHmhi4SxkXgMMUx2Nl8Twwv1BrRF0e5cjSPy5HO2TCOaahsUYY35s3yDc5VC277FqbQWv6UdC6sZNw==";
    private const string ScriptManifest = """{"schemaVersion":1,"product":"DynamicIsland","version":"1.3.0","files":[{"name":"DynamicIsland-Setup-win-x64.exe","size":100,"sha256":"57e8310931615cb786e0923d1ef88d4ad9f0ab74bf85a807f77fe2a8915001e4"}]}""";
    private const string ScriptSignature = "yo1e2hdaJOv5inJC8WESbAj2WoygMUITnF5rlPJomJ0IBbNPs55X6usQb9kv3GenXZskBv3y6KZKtKzFgW5r5g==";

    [Fact]
    public void A_manifest_signed_by_the_release_scripts_verifies_and_parses()
    {
        var bytes = Encoding.UTF8.GetBytes(ScriptManifest);

        Assert.True(ReleaseManifest.IsSignedBy(bytes, ScriptSignature, [ScriptPublicKey]));
        var manifest = ReleaseManifest.Parse(bytes)!;
        Assert.Equal("1.3.0", manifest.Version.ToString());
        Assert.Equal(new ManifestFile("DynamicIsland-Setup-win-x64.exe", 100, "57e8310931615cb786e0923d1ef88d4ad9f0ab74bf85a807f77fe2a8915001e4"),
            manifest.Find("dynamicisland-setup-win-x64.exe"));
    }

    [Fact]
    public void Any_change_to_the_manifest_or_signature_fails()
    {
        var tampered = Encoding.UTF8.GetBytes(ScriptManifest.Replace("\"size\":100", "\"size\":101", StringComparison.Ordinal));
        var badSignature = "A" + ScriptSignature[1..];

        Assert.False(ReleaseManifest.IsSignedBy(tampered, ScriptSignature, [ScriptPublicKey]));
        Assert.False(ReleaseManifest.IsSignedBy(Encoding.UTF8.GetBytes(ScriptManifest), badSignature, [ScriptPublicKey]));
        Assert.False(ReleaseManifest.IsSignedBy(Encoding.UTF8.GetBytes(ScriptManifest), "not base64!", [ScriptPublicKey]));
        Assert.False(ReleaseManifest.IsSignedBy(Encoding.UTF8.GetBytes(ScriptManifest), null, [ScriptPublicKey]));
    }

    [Fact]
    public void Only_a_pinned_key_counts_and_a_second_key_allows_rotation()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var otherKey = Convert.ToBase64String(other.ExportSubjectPublicKeyInfo());
        var bytes = Encoding.UTF8.GetBytes(ScriptManifest);

        Assert.False(ReleaseManifest.IsSignedBy(bytes, ScriptSignature, [otherKey]));
        Assert.False(ReleaseManifest.IsSignedBy(bytes, ScriptSignature, []));
        Assert.False(ReleaseManifest.IsSignedBy(bytes, ScriptSignature, ["garbage"]));
        Assert.True(ReleaseManifest.IsSignedBy(bytes, ScriptSignature, ["garbage", otherKey, ScriptPublicKey]));
    }

    [Fact]
    public void A_p384_key_is_not_accepted()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var bytes = Encoding.UTF8.GetBytes(ScriptManifest);
        var signature = Convert.ToBase64String(p384.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

        Assert.False(ReleaseManifest.IsSignedBy(bytes, signature, [Convert.ToBase64String(p384.ExportSubjectPublicKeyInfo())]));
    }

    [Theory]
    [InlineData("""{"schemaVersion":2,"product":"DynamicIsland","version":"1.3.0","files":[{"name":"a.exe","size":1,"sha256":"HASH"}]}""")]
    [InlineData("""{"schemaVersion":1,"product":"Other","version":"1.3.0","files":[{"name":"a.exe","size":1,"sha256":"HASH"}]}""")]
    [InlineData("""{"schemaVersion":1,"product":"DynamicIsland","version":"latest","files":[{"name":"a.exe","size":1,"sha256":"HASH"}]}""")]
    [InlineData("""{"schemaVersion":1,"product":"DynamicIsland","version":"1.3.0","files":[]}""")]
    [InlineData("""{"schemaVersion":1,"product":"DynamicIsland","version":"1.3.0","files":[{"name":"../a.exe","size":1,"sha256":"HASH"}]}""")]
    [InlineData("""{"schemaVersion":1,"product":"DynamicIsland","version":"1.3.0","files":[{"name":"a.exe","size":0,"sha256":"HASH"}]}""")]
    [InlineData("""{"schemaVersion":1,"product":"DynamicIsland","version":"1.3.0","files":[{"name":"a.exe","size":1,"sha256":"ABC"}]}""")]
    [InlineData("""{"schemaVersion":1,"product":"DynamicIsland","version":"1.3.0","files":[{"name":"a.exe","size":1,"sha256":"HASH"},{"name":"A.EXE","size":1,"sha256":"HASH"}]}""")]
    [InlineData("""[1,2]""")]
    [InlineData("""{ broken""")]
    public void Malformed_manifests_are_rejected(string json) =>
        Assert.Null(ReleaseManifest.Parse(Encoding.UTF8.GetBytes(json.Replace("HASH", new string('a', 64), StringComparison.Ordinal))));
}
