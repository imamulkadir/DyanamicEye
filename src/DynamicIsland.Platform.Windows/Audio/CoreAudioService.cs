using System.Runtime.InteropServices;
using DynamicIsland.Core.Audio;

namespace DynamicIsland.Platform.Windows.Audio;

/// <summary>
/// <see cref="IAudioService"/> over Core Audio (ADR 0022): master volume and mute of the default render endpoint, with
/// Windows' own change notifications (no polling). Follows default-device changes and removal. Every COM failure means
/// "no device" or "not changed". Callbacks arrive on Windows audio threads; all state is guarded.
/// </summary>
public sealed class CoreAudioService : IAudioService
{
    private const uint ClsctxAll = 0x17;
    private const int SOk = 0;

    /// <summary>Identifies changes made by this app in notifications (kept for diagnostics; the state is re-read either way).</summary>
    private static Guid s_eventContext = new("6F0B4D5E-3C6B-4C9A-9E0E-5B1A2C7D8E91");

    private readonly Lock _gate = new();
    private readonly NotificationClient _deviceEvents;
    private readonly VolumeCallback _volumeEvents;
    private IMMDeviceEnumerator? _enumerator;
    private IAudioEndpointVolume? _endpoint;
    private bool _disposed;

    public CoreAudioService()
    {
        _deviceEvents = new NotificationClient(this);
        _volumeEvents = new VolumeCallback(this);
    }

    public event EventHandler? Changed;

    public bool Start()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            lock (_gate)
            {
                _enumerator = enumerator;
            }

            enumerator.RegisterEndpointNotificationCallback(_deviceEvents);
            Reconnect();
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return false;
        }
    }

    public AudioState GetState()
    {
        lock (_gate)
        {
            if (_endpoint is not { } endpoint)
            {
                return AudioState.NoDevice;
            }

            try
            {
                return endpoint.GetMasterVolumeLevelScalar(out var level) == SOk && endpoint.GetMute(out var muted) == SOk
                    ? new AudioState(true, Math.Clamp(level, 0f, 1f), muted)
                    : AudioState.NoDevice;
            }
            catch (COMException)
            {
                return AudioState.NoDevice;
            }
        }
    }

    public bool SetVolume(float volume)
    {
        lock (_gate)
        {
            try
            {
                return _endpoint is { } endpoint && endpoint.SetMasterVolumeLevelScalar(Math.Clamp(volume, 0f, 1f), ref s_eventContext) == SOk;
            }
            catch (COMException)
            {
                return false;
            }
        }
    }

    public bool SetMuted(bool muted)
    {
        lock (_gate)
        {
            try
            {
                return _endpoint is { } endpoint && endpoint.SetMute(muted, ref s_eventContext) == SOk;
            }
            catch (COMException)
            {
                return false;
            }
        }
    }

    public void Dispose()
    {
        IMMDeviceEnumerator? enumerator;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            enumerator = _enumerator;
            _enumerator = null;
            ReleaseEndpoint();
        }

        if (enumerator is not null)
        {
            try
            {
                enumerator.UnregisterEndpointNotificationCallback(_deviceEvents);
            }
            catch (COMException)
            {
                // Audio service already gone.
            }

            Marshal.ReleaseComObject(enumerator);
        }
    }

    /// <summary>Binds to the current default render device (or to none) and raises <see cref="Changed"/>.</summary>
    private void Reconnect()
    {
        lock (_gate)
        {
            if (_disposed || _enumerator is not { } enumerator)
            {
                return;
            }

            ReleaseEndpoint();
            try
            {
                if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device) == SOk && device is not null)
                {
                    var iid = typeof(IAudioEndpointVolume).GUID;
                    if (device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out var instance) == SOk && instance is IAudioEndpointVolume endpoint)
                    {
                        endpoint.RegisterControlChangeNotify(_volumeEvents);
                        _endpoint = endpoint;
                    }

                    Marshal.ReleaseComObject(device);
                }
            }
            catch (COMException)
            {
                // No usable output device right now; the next device notification retries.
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Caller holds <see cref="_gate"/>.</summary>
    private void ReleaseEndpoint()
    {
        if (_endpoint is null)
        {
            return;
        }

        try
        {
            _endpoint.UnregisterControlChangeNotify(_volumeEvents);
        }
        catch (COMException)
        {
            // Device already removed.
        }

        Marshal.ReleaseComObject(_endpoint);
        _endpoint = null;
    }

    private void OnVolumeNotification() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Default device changes and removals (Windows audio threads; must return quickly and never throw).</summary>
    [ComVisible(true)]
    private sealed class NotificationClient(CoreAudioService owner) : IMMNotificationClient
    {
        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
        {
            if (flow == EDataFlow.Render && role == ERole.Multimedia)
            {
                // Re-bind off the notification thread: Core Audio forbids blocking calls in its callbacks.
                ThreadPool.QueueUserWorkItem(_ => owner.Reconnect());
            }
        }

        public void OnDeviceStateChanged(string deviceId, uint newState) => ThreadPool.QueueUserWorkItem(_ => owner.Reconnect());

        public void OnDeviceAdded(string deviceId)
        {
        }

        public void OnDeviceRemoved(string deviceId) => ThreadPool.QueueUserWorkItem(_ => owner.Reconnect());

        public void OnPropertyValueChanged(string deviceId, PropertyKey key)
        {
        }
    }

    /// <summary>Volume or mute changed on the bound endpoint, by anyone.</summary>
    [ComVisible(true)]
    private sealed class VolumeCallback(CoreAudioService owner) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr notificationData)
        {
            ThreadPool.QueueUserWorkItem(_ => owner.OnVolumeNotification());
            return SOk;
        }
    }
}
