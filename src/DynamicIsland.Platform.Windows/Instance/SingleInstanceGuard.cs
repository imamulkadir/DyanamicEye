using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace DynamicIsland.Platform.Windows.Instance;

/// <summary>
/// Per-user single instance (guide §4.4).
/// <list type="bullet">
/// <item>Ownership: named mutex <c>Local\{appId}.{userSid}.instance</c>; held while the guard lives.</item>
/// <item>Activation: a second instance sends the line <c>activate</c> over the named pipe
/// <c>{appId}.{userSid}.activate</c> and waits for <c>ok</c> (accepted) or <c>no</c> (rejected), so success means the
/// running instance really received the request. The pipe ACL grants only the current user and the client requires the
/// server to be owned by the current user, so another user cannot squat the pipe or impersonate the app.</item>
/// <item>Limits: one line of at most 64 ASCII bytes within 2 seconds; anything else is rejected.</item>
/// </list>
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    public const string ActivateCommand = "activate";
    public const string AcceptedResponse = "ok";
    public const string RejectedResponse = "no";

    private const byte LineEnd = (byte)'\n';
    private const int MaxLineBytes = 64;
    private const int MaxDrainBytes = 64 * 1024;
    private static readonly TimeSpan ExchangeTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _listening = new();
    private Task? _listener;

    private SingleInstanceGuard(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
    }

    /// <returns>The guard when this is the first instance for the current user; otherwise <c>null</c>.</returns>
    public static SingleInstanceGuard? TryAcquire(string appId)
    {
        var names = InstanceNames.For(appId);
        var mutex = new Mutex(initiallyOwned: false, names.Mutex, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstanceGuard(mutex, names.Pipe);
    }

    /// <summary>Asks the running instance to show itself.</summary>
    /// <returns><c>true</c> only when the running instance accepted the request within <paramref name="timeout"/>.</returns>
    public static async Task<bool> SignalExistingInstanceAsync(string appId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var names = InstanceNames.For(appId);

        // Best effort; failure only means Windows may flash the taskbar instead of focusing the window.
        _ = NativeMethods.AllowSetForegroundWindow(NativeMethods.AllowAnyProcess);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        using var client = new NamedPipeClientStream(".", names.Pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await client.ConnectAsync(deadline.Token).ConfigureAwait(false);
            await WriteLineAsync(client, ActivateCommand, deadline.Token).ConfigureAwait(false);
            return await ReadLineAsync(client, deadline.Token).ConfigureAwait(false) == AcceptedResponse;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Starts accepting activation requests. Callbacks run on a background thread.</summary>
    /// <param name="onActivate">Invoked for each valid request, before the client is answered.</param>
    /// <param name="onListenerFailed">Invoked once if the pipe cannot be created (activation then stays unavailable).</param>
    public void StartListening(Action onActivate, Action<Exception> onListenerFailed)
    {
        ArgumentNullException.ThrowIfNull(onActivate);
        ArgumentNullException.ThrowIfNull(onListenerFailed);
        if (_listener is not null)
        {
            throw new InvalidOperationException("Already listening.");
        }

        _listener = Task.Run(() => ListenAsync(onActivate, onListenerFailed, _listening.Token));
    }

    public void Dispose()
    {
        _listening.Cancel();
        _listener?.Wait(StopTimeout);
        _listening.Dispose();
        _mutex.Dispose();
    }

    /// <summary>Only the current user may create, read or write the pipe; inherited entries are removed.</summary>
    internal static PipeSecurity CreatePipeSecurity()
    {
        var user = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("The current Windows user has no security identifier.");

        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    private async Task ListenAsync(Action onActivate, Action<Exception> onListenerFailed, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = NamedPipeServerStreamAcl.Create(
                    _pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
                    inBufferSize: MaxLineBytes,
                    outBufferSize: MaxLineBytes,
                    CreatePipeSecurity());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                onListenerFailed(ex);
                return;
            }

            await using (server.ConfigureAwait(false))
            {
                try
                {
                    await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    await HandleClientAsync(server, onActivate, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (IOException)
                {
                    // Client vanished mid-exchange; wait for the next one.
                }
            }
        }
    }

    private static async Task HandleClientAsync(NamedPipeServerStream server, Action onActivate, CancellationToken cancellationToken)
    {
        using var exchange = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        exchange.CancelAfter(ExchangeTimeout);

        string? command;
        try
        {
            command = await ReadLineAsync(server, exchange.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            command = null;
        }

        var accepted = command == ActivateCommand;
        if (accepted)
        {
            onActivate();
        }

        using var reply = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        reply.CancelAfter(ExchangeTimeout);
        try
        {
            await WriteLineAsync(server, accepted ? AcceptedResponse : RejectedResponse, reply.Token).ConfigureAwait(false);

            // Keep the instance until the client has read the reply and closed (or the timeout passes).
            await DrainUntilClosedAsync(server, reply.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Slow or stuck client: drop it.
        }
    }

    private static async Task WriteLineAsync(PipeStream pipe, string line, CancellationToken cancellationToken)
    {
        var bytes = Encoding.ASCII.GetBytes(line + "\n");
        await pipe.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <returns>The ASCII line without its terminator, or <c>null</c> when it is too long, not ASCII or not terminated.</returns>
    private static async Task<string?> ReadLineAsync(PipeStream pipe, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxLineBytes];
        var length = 0;
        var single = new byte[1];

        while (length < MaxLineBytes)
        {
            if (await pipe.ReadAsync(single, cancellationToken).ConfigureAwait(false) == 0)
            {
                return null;
            }

            if (single[0] == LineEnd)
            {
                var line = buffer.AsSpan(0, length);
                return Ascii.IsValid(line) ? Encoding.ASCII.GetString(line) : null;
            }

            buffer[length++] = single[0];
        }

        return null;
    }

    private static async Task DrainUntilClosedAsync(PipeStream pipe, CancellationToken cancellationToken)
    {
        var scratch = new byte[MaxLineBytes];
        var drained = 0;
        int read;
        while (drained < MaxDrainBytes && (read = await pipe.ReadAsync(scratch, cancellationToken).ConfigureAwait(false)) > 0)
        {
            drained += read;
        }
    }

    private readonly record struct InstanceNames(string Mutex, string Pipe)
    {
        public static InstanceNames For(string appId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(appId);
            if (!appId.All(c => char.IsAsciiLetterOrDigit(c) || c == '.'))
            {
                throw new ArgumentException("App id may contain only ASCII letters, digits and dots.", nameof(appId));
            }

            var sid = WindowsIdentity.GetCurrent().User?.Value
                ?? throw new InvalidOperationException("The current Windows user has no security identifier.");

            return new InstanceNames($@"Local\{appId}.{sid}.instance", $"{appId}.{sid}.activate");
        }
    }
}
