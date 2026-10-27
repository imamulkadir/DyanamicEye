using System.Text.Json;

namespace DynamicIsland.Core.Distribution;

/// <summary>Who turned self-update off (ADR 0031).</summary>
public enum UpdatePolicySource
{
    None,

    /// <summary><c>HKLM\Software\Policies\DynamicIsland\DisableSelfUpdate</c> (Group Policy, Intune, IT scripts).</summary>
    MachinePolicy,

    /// <summary><c>HKCU\Software\Policies\DynamicIsland\DisableSelfUpdate</c> (user policy; normally only IT can write it).</summary>
    UserPolicy,

    /// <summary><c>managed.json</c> next to the launcher, written by the installer's <c>/MANAGED</c> switch.</summary>
    InstallerFlag,
}

/// <summary>Whether this copy may update itself (guide P06-02, §7: IT-managed copies never self-install).</summary>
public sealed record UpdatePolicy(bool SelfUpdateDisabled, UpdatePolicySource Source)
{
    public const string PolicyKey = @"Software\Policies\DynamicIsland";
    public const string DisableSelfUpdateValue = "DisableSelfUpdate";
    public const string ManagedFileName = "managed.json";

    public static UpdatePolicy Allowed { get; } = new(false, UpdatePolicySource.None);

    /// <summary>
    /// Machine policy wins, then user policy, then the installer flag. A policy value of 0 explicitly allows updates
    /// at that level (so a machine policy of 0 overrides an installer flag); anything else non-zero disables them.
    /// </summary>
    /// <param name="machineValue">The HKLM DWORD, or <c>null</c> when absent or not a DWORD.</param>
    /// <param name="userValue">The HKCU DWORD, or <c>null</c>.</param>
    /// <param name="managedJson">Contents of <c>managed.json</c>, or <c>null</c> when there is none.</param>
    public static UpdatePolicy Resolve(int? machineValue, int? userValue, string? managedJson)
    {
        if (machineValue is { } machine)
        {
            return machine != 0 ? new UpdatePolicy(true, UpdatePolicySource.MachinePolicy) : Allowed;
        }

        if (userValue is { } user)
        {
            return user != 0 ? new UpdatePolicy(true, UpdatePolicySource.UserPolicy) : Allowed;
        }

        return ManagedFileDisables(managedJson) ? new UpdatePolicy(true, UpdatePolicySource.InstallerFlag) : Allowed;
    }

    /// <summary>The installer writes <c>{ "disableSelfUpdate": true }</c>; an unreadable file fails closed (managed).</summary>
    private static bool ManagedFileDisables(string? json)
    {
        if (json is null)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return !(document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("disableSelfUpdate", out var value)
                && value.ValueKind == JsonValueKind.False);
        }
        catch (JsonException)
        {
            return true;
        }
    }
}
