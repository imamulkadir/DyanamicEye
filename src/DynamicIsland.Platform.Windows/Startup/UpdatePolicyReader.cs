using System.Security;
using DynamicIsland.Core.Distribution;
using Microsoft.Win32;

namespace DynamicIsland.Platform.Windows.Startup;

/// <summary>Reads the IT self-update policy (ADR 0031): registry policies and the installer's <c>managed.json</c>.</summary>
public static class UpdatePolicyReader
{
    private const int MaxManagedFileBytes = 4096;

    /// <param name="layoutRoot">Folder of the stable launcher (where the installer writes <c>managed.json</c>).</param>
    public static UpdatePolicy Read(string layoutRoot) =>
        UpdatePolicy.Resolve(ReadDword(Registry.LocalMachine), ReadDword(Registry.CurrentUser), ReadManagedFile(layoutRoot));

    private static int? ReadDword(RegistryKey hive)
    {
        try
        {
            using var key = hive.OpenSubKey(UpdatePolicy.PolicyKey);
            return key?.GetValue(UpdatePolicy.DisableSelfUpdateValue) is int value ? value : null;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static string? ReadManagedFile(string layoutRoot)
    {
        var path = Path.Combine(layoutRoot, UpdatePolicy.ManagedFileName);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            // Present but oversized or unreadable: treat as managed (fail closed), like a corrupt file.
            return new FileInfo(path).Length <= MaxManagedFileBytes ? File.ReadAllText(path) : "{}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "{}";
        }
    }
}
