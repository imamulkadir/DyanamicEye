using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using DynamicIsland.Platform.Windows.Instance;

namespace DynamicIsland.IntegrationTests;

public sealed class SingleInstanceGuardTests
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(5);

    private readonly string _appId = "DynamicIsland.Tests." + Guid.NewGuid().ToString("N");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Only_one_guard_per_user_can_be_acquired()
    {
        using var first = SingleInstanceGuard.TryAcquire(_appId);
        using var second = SingleInstanceGuard.TryAcquire(_appId);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void A_running_copy_this_process_may_not_open_counts_as_the_running_instance()
    {
        // Like a copy started with "Run as administrator": its mutex exists, but a normal process may only wait on it.
        var sid = WindowsIdentity.GetCurrent().User!;
        var security = new MutexSecurity();
        security.AddAccessRule(new MutexAccessRule(sid, MutexRights.Synchronize, AccessControlType.Allow));
        using var elevated = MutexAcl.Create(initiallyOwned: false, $@"Local\{_appId}.{sid.Value}.instance", out var created, security);
        Assert.True(created);

        var exception = Record.Exception(() => SingleInstanceGuard.TryAcquire(_appId)?.Dispose());
        using var guard = SingleInstanceGuard.TryAcquire(_appId);

        Assert.Null(exception);
        Assert.Null(guard);
    }

    [Fact]
    public void Guard_can_be_reacquired_after_release()
    {
        SingleInstanceGuard.TryAcquire(_appId)!.Dispose();

        using var again = SingleInstanceGuard.TryAcquire(_appId);

        Assert.NotNull(again);
    }

    [Fact]
    public async Task Accepted_activation_has_already_reached_the_running_instance()
    {
        using var guard = SingleInstanceGuard.TryAcquire(_appId)!;
        var count = 0;
        guard.StartListening(() => Interlocked.Increment(ref count), _ => { });

        var accepted = await SingleInstanceGuard.SignalExistingInstanceAsync(_appId, SignalTimeout, Ct);

        Assert.True(accepted);
        Assert.Equal(1, Volatile.Read(ref count));
    }

    [Fact]
    public async Task Repeated_activations_are_all_delivered()
    {
        using var guard = SingleInstanceGuard.TryAcquire(_appId)!;
        var count = 0;
        guard.StartListening(() => Interlocked.Increment(ref count), _ => { });

        for (var i = 0; i < 3; i++)
        {
            Assert.True(await SingleInstanceGuard.SignalExistingInstanceAsync(_appId, SignalTimeout, Ct));
        }

        Assert.Equal(3, Volatile.Read(ref count));
    }

    [Theory]
    [InlineData("hello\n")]
    [InlineData("ACTIVATE\n")]
    [InlineData("activate \n")]
    [InlineData("activaté\n")]
    public async Task Unknown_commands_are_rejected_and_listening_continues(string message)
    {
        using var guard = SingleInstanceGuard.TryAcquire(_appId)!;
        var count = 0;
        guard.StartListening(() => Interlocked.Increment(ref count), _ => { });

        var response = await SendRawAsync(Encoding.UTF8.GetBytes(message));

        Assert.Equal(SingleInstanceGuard.RejectedResponse, response);
        Assert.Equal(0, Volatile.Read(ref count));
        Assert.True(await SingleInstanceGuard.SignalExistingInstanceAsync(_appId, SignalTimeout, Ct));
        Assert.Equal(1, Volatile.Read(ref count));
    }

    [Fact]
    public async Task Unterminated_command_times_out_and_is_rejected()
    {
        using var guard = SingleInstanceGuard.TryAcquire(_appId)!;
        var count = 0;
        guard.StartListening(() => Interlocked.Increment(ref count), _ => { });

        var response = await SendRawAsync(Encoding.ASCII.GetBytes(SingleInstanceGuard.ActivateCommand));

        Assert.Equal(SingleInstanceGuard.RejectedResponse, response);
        Assert.Equal(0, Volatile.Read(ref count));
    }

    [Fact]
    public async Task Oversized_message_is_rejected()
    {
        using var guard = SingleInstanceGuard.TryAcquire(_appId)!;
        var count = 0;
        guard.StartListening(() => Interlocked.Increment(ref count), _ => { });

        var response = await SendRawAsync(Encoding.ASCII.GetBytes(new string('a', 4096) + "\n"));

        Assert.Equal(SingleInstanceGuard.RejectedResponse, response);
        Assert.Equal(0, Volatile.Read(ref count));
        Assert.True(await SingleInstanceGuard.SignalExistingInstanceAsync(_appId, SignalTimeout, Ct));
    }

    [Fact]
    public async Task Signal_without_a_running_instance_returns_false()
    {
        var delivered = await SingleInstanceGuard.SignalExistingInstanceAsync(_appId, TimeSpan.FromMilliseconds(300), Ct);

        Assert.False(delivered);
    }

    [Fact]
    public void Pipe_acl_grants_only_the_current_user_and_blocks_inheritance()
    {
        var security = SingleInstanceGuard.CreatePipeSecurity();
        var currentUser = WindowsIdentity.GetCurrent().User;

        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<PipeAccessRule>()
            .ToArray();

        Assert.True(security.AreAccessRulesProtected);
        var rule = Assert.Single(rules);
        Assert.Equal(currentUser, rule.IdentityReference);
        Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
    }

    [Fact]
    public void App_id_with_unsafe_characters_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => SingleInstanceGuard.TryAcquire(@"bad\name"));
        Assert.Throws<ArgumentException>(() => SingleInstanceGuard.TryAcquire(" "));
    }

    /// <summary>Sends arbitrary bytes and returns the server's reply line.</summary>
    private async Task<string?> SendRawAsync(byte[] payload)
    {
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        using var client = new NamedPipeClientStream(".", $"{_appId}.{sid}.activate", PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync((int)SignalTimeout.TotalMilliseconds, Ct);
        await client.WriteAsync(payload, Ct);
        await client.FlushAsync(Ct);

        using var reader = new StreamReader(client, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        return await reader.ReadLineAsync(Ct);
    }
}
