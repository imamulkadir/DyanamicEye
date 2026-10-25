using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace DynamicIsland.Platform.Windows.Instance;

/// <summary>
/// Per-user single instance (guide §4.4).
/// <list type="bullet">
/// <item>Ownership: named mutex <c>Local\{appId}.{userSid}.instance</c>; held while the guard lives.</item>
/// <item>Activation: a second instance sends the fixed command <see cref="ActivateCommand"/> over the named pipe
/// <c>{appId}.{userSid}.activate</c>. The pipe ACL grants only the current user; the client requires the server to be
/// owned by the current user, so another user cannot squat the pipe or impersonate the app.</item>
/// </list>
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    public const string ActivateCommand = "activate";

    private const int MaxMessageBytes = 64;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

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
    /// <returns><c>false</c> when no instance answered within <paramref name="timeout"/>.</returns>
    public static async Task<bool> SignalExistingInstanceAsync(string appId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var names = InstanceNames.For(appId);

        // Best effort; failure only means Windows may flash the taskbar instead of focusing the window.
        _ = NativeMethods.AllowSetForegroundWindow(NativeMethods.AllowAnyProcess);

        using var client = new NamedPipeClientStream(".", names.Pipe, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await client.ConnectAsync((int)timeout.TotalMilliseconds, cancellationToken).ConfigureAwait(false);
            await client.WriteAsync(Encoding.ASCII.GetBytes(ActivateCommand), cancellationToken).ConfigureAwait(false);
            await client.FlushAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Starts accepting activation requests. Callbacks run on a background thread.</summary>
    /// <param name="onActivate">Invoked for each valid activation request.</param>
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
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
                    inBufferSize: MaxMessageBytes,
                    outBufferSize: 0,
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
                    if (await ReadCommandAsync(server, cancellationToken).ConfigureAwait(false) == ActivateCommand)
                    {
                        onActivate();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (IOException)
                {
                    // Client disconnected mid-message; wait for the next one.
                }
            }
        }
    }

    /// <returns>The ASCII command, or <c>null</c> when the message is too long, too slow or not ASCII.</returns>
    private static async Task<string?> ReadCommandAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadTimeout);

        var buffer = new byte[MaxMessageBytes + 1];
        var length = 0;
        try
        {
            int read;
            while ((read = await server.ReadAsync(buffer.AsMemory(length), timeout.Token).ConfigureAwait(false)) > 0)
            {
                length += read;
                if (length > MaxMessageBytes)
                {
                    return null;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        var bytes = buffer.AsSpan(0, length);
        return Ascii.IsValid(bytes) ? Encoding.ASCII.GetString(bytes) : null;
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
