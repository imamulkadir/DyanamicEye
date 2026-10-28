using System.Security.AccessControl;
using System.Security.Principal;
using DynamicIsland.Core.Distribution;
using DynamicIsland.Infrastructure.Updates;

namespace DynamicIsland.IntegrationTests;

/// <summary>The in-place update check (P06-08, ADR 0037) against a real Windows ACL that denies creating files.</summary>
public sealed class UpdateLayoutAccessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DynamicIsland.IntegrationTests", Guid.NewGuid().ToString("N"));

    public UpdateLayoutAccessTests()
    {
        Directory.CreateDirectory(Payload);
        File.WriteAllText(Path.Combine(_root, AppLayout.PointerFileName), """{"current":"1.0.0"}""");
        File.WriteAllText(Path.Combine(_root, AppLayout.RootLauncher), "launcher");
    }

    private string Payload => AppLayout.PayloadFolder(_root, "1.0.0");

    public void Dispose()
    {
        SetDenyCreate(false);
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void A_layout_the_user_cannot_write_is_not_updated_in_place()
    {
        Assert.True(UpdateHandover.CanUpdateInPlace(Payload));

        SetDenyCreate(true);
        Assert.False(UpdateHandover.CanUpdateInPlace(Payload));
        Assert.Empty(Directory.GetFiles(_root, ".write-test-*"));

        SetDenyCreate(false);
        Assert.True(UpdateHandover.CanUpdateInPlace(Payload));
    }

    [Fact]
    public void A_copy_outside_the_release_layout_is_never_updated_in_place()
    {
        Assert.False(UpdateHandover.CanUpdateInPlace(_root));
    }

    private void SetDenyCreate(bool deny)
    {
        var folder = new DirectoryInfo(_root);
        var security = folder.GetAccessControl();
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.CreateFiles, AccessControlType.Deny);
        if (deny)
        {
            security.AddAccessRule(rule);
        }
        else
        {
            security.RemoveAccessRuleSpecific(rule);
        }

        folder.SetAccessControl(security);
    }
}
