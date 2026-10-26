using DynamicIsland.Core.Startup;
using Microsoft.Win32;

namespace DynamicIsland.Platform.Windows.Startup;

/// <summary>
/// Sign-in startup through the per-user Run key (ADR 0015). Reads, but never writes, the StartupApproved state that
/// Settings › Apps › Startup and Task Manager keep, and the <c>DisableCurrentUserRun</c> policy. Standard-user only.
/// </summary>
public sealed class RegistryStartupRegistration : IStartupRegistration
{
    public const string DefaultValueName = "DynamicIsland";
    public const string DefaultRoot = @"Software\Microsoft\Windows\CurrentVersion";

    private const string PolicyValue = "DisableCurrentUserRun";

    private readonly string _runKey;
    private readonly string _approvedKey;
    private readonly string _policyKey;
    private readonly string _valueName;
    private readonly bool _checkMachinePolicy;

    /// <param name="userRoot">Key under HKCU standing for <c>Software\Microsoft\Windows\CurrentVersion</c> (tests use a scratch key).</param>
    /// <param name="checkMachinePolicy">Also honour the HKLM policy (off for tests with a scratch root).</param>
    public RegistryStartupRegistration(string userRoot = DefaultRoot, string valueName = DefaultValueName, bool checkMachinePolicy = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);

        _runKey = userRoot + @"\Run";
        _approvedKey = userRoot + @"\Explorer\StartupApproved\Run";
        _policyKey = userRoot + @"\Policies\Explorer";
        _valueName = valueName;
        _checkMachinePolicy = checkMachinePolicy;
    }

    public StartupRegistrationStatus GetStatus()
    {
        using var run = Registry.CurrentUser.OpenSubKey(_runKey);
        var command = run?.GetValue(_valueName) as string;
        return new StartupRegistrationStatus(string.IsNullOrWhiteSpace(command) ? null : command, IsDisabledInWindows(), IsBlockedByPolicy());
    }

    public void Register(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        using var run = Registry.CurrentUser.CreateSubKey(_runKey, writable: true);
        run.SetValue(_valueName, command, RegistryValueKind.String);
    }

    public void Unregister()
    {
        using var run = Registry.CurrentUser.OpenSubKey(_runKey, writable: true);
        run?.DeleteValue(_valueName, throwOnMissingValue: false);
    }

    /// <summary>StartupApproved data: the first byte is even (e.g. 02) when enabled and odd (e.g. 03) when the user turned it off.</summary>
    private bool IsDisabledInWindows()
    {
        using var approved = Registry.CurrentUser.OpenSubKey(_approvedKey);
        return approved?.GetValue(_valueName) is byte[] { Length: > 0 } data && (data[0] & 1) == 1;
    }

    private bool IsBlockedByPolicy() =>
        ReadPolicy(Registry.CurrentUser, _policyKey)
        || (_checkMachinePolicy && ReadPolicy(Registry.LocalMachine, DefaultRoot + @"\Policies\Explorer"));

    private static bool ReadPolicy(RegistryKey hive, string path)
    {
        using var policy = hive.OpenSubKey(path);
        return policy?.GetValue(PolicyValue) is int value && value != 0;
    }
}
