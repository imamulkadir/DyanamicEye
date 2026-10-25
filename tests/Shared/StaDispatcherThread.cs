using System.Windows.Threading;

namespace DynamicIsland.TestSupport;

/// <summary>
/// Dedicated STA thread running a WPF dispatcher, so tests can create real windows and keep them alive (pumping
/// messages) while the test awaits. Linked into the integration and smoke test projects.
/// </summary>
internal sealed class StaDispatcherThread : IDisposable
{
    private readonly Thread _thread;
    private readonly Dispatcher _dispatcher;

    public StaDispatcherThread()
    {
        using var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;
        _thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Test STA dispatcher",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();
        _dispatcher = dispatcher!;
    }

    public T Invoke<T>(Func<T> action) => _dispatcher.Invoke(action);

    public void Invoke(Action action) => _dispatcher.Invoke(action);

    public void Dispose()
    {
        _dispatcher.InvokeShutdown();
        _thread.Join(TimeSpan.FromSeconds(5));
    }
}
