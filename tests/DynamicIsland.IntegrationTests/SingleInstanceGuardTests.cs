using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using DynamicIsland.Platform.Windows.Instance;

namespace DynamicIsland.IntegrationTests;

public sealed class SingleInstanceGuardTests
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

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
    public void Guard_can_be_reacquired_after_release()
    {
        SingleInstanceGuard.TryAcquire(_appId)!.Dispose();

        using var again = SingleInstanceGuard.TryAcquire(_appId);

        Assert.NotNull(again);
    }

    [Fact]
    public async Task Activation_request_reaches_the_running_instance()
    {
        using var guard = SingleInstanceGuard.TryAcquire(_appId)!;
        var activated = new TaskCompletionSource();
        guard.StartListening(() => activated.TrySetResult(), ex => activated.TrySetException(ex));

        var delivered = await SingleInstanceGuard.SignalExistingInstanceAsync(_appId, SignalTimeout, Ct);

        Assert.True(delivered);
        await activated.Task.WaitAsync(WaitTimeout, Ct);
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

        await WaitUntilAsync(() => Volatile.Read(ref count) == 3);
        Assert.Equal(3, count);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("ACTIVATE")]
    [InlineData("activate ")]
    public async Task Unknown_commands_are_ignored_and_listening_continues(string message)
    {
        using var guard = SingleInstanceGuard.TryAcquire(_appId)!;
        var count = 0;
        guard.StartListening(() => Interlocked.Increment(ref count), _ => { });

        await SendRawAsync(Encoding.ASCII.GetBytes(message));
        Assert.True(await SingleInstanceGuard.SignalExistingInstanceAsync(_appId, SignalTimeout, Ct));

        await WaitUntilAsync(() => Volatile.Read(ref count) >= 1);
        await Task.Delay(100, Ct);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Oversized_message_is_rejected()
    {
        using var guard = SingleInstanceGuard.TryAcquire(_appId)!;
        var count = 0;
        guard.StartListening(() => Interlocked.Increment(ref count), _ => { });

        await SendRawAsync(Encoding.ASCII.GetBytes(SingleInstanceGuard.ActivateCommand + new string(' ', 4096)));
        Assert.True(await SingleInstanceGuard.SignalExistingInstanceAsync(_appId, SignalTimeout, Ct));

        await WaitUntilAsync(() => Volatile.Read(ref count) >= 1);
        Assert.Equal(1, count);
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

    private async Task SendRawAsync(byte[] payload)
    {
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        using var client = new NamedPipeClientStream(".", $"{_appId}.{sid}.activate", PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync((int)SignalTimeout.TotalMilliseconds, Ct);
        try
        {
            await client.WriteAsync(payload, Ct);
        }
        catch (IOException)
        {
            // The server may close the pipe as soon as the message exceeds its limit.
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, Ct);
        }
    }
}
