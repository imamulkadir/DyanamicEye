using DynamicIsland.Core.Distribution;

namespace DynamicIsland.UnitTests.Distribution;

/// <summary>IT-managed self-update policy (P06-02, ADR 0031).</summary>
public sealed class UpdatePolicyTests
{
    [Theory]
    [InlineData(null, null, null, false, UpdatePolicySource.None)]
    [InlineData(1, null, null, true, UpdatePolicySource.MachinePolicy)]
    [InlineData(null, 1, null, true, UpdatePolicySource.UserPolicy)]
    [InlineData(null, null, """{ "disableSelfUpdate": true }""", true, UpdatePolicySource.InstallerFlag)]
    [InlineData(0, 1, """{ "disableSelfUpdate": true }""", false, UpdatePolicySource.None)] // Machine policy 0 explicitly allows.
    [InlineData(null, 0, """{ "disableSelfUpdate": true }""", false, UpdatePolicySource.None)]
    [InlineData(2, 0, null, true, UpdatePolicySource.MachinePolicy)]
    [InlineData(null, null, """{ "disableSelfUpdate": false }""", false, UpdatePolicySource.None)]
    public void Machine_policy_wins_then_user_policy_then_the_installer_flag(int? machine, int? user, string? file, bool disabled, UpdatePolicySource source) =>
        Assert.Equal(new UpdatePolicy(disabled, source), UpdatePolicy.Resolve(machine, user, file));

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{ "disableSelfUpdate": "no" }""")]
    public void A_present_but_unclear_managed_file_fails_closed(string file) =>
        Assert.Equal(new UpdatePolicy(true, UpdatePolicySource.InstallerFlag), UpdatePolicy.Resolve(null, null, file));
}
