namespace DynamicIsland.Core.Audio;

/// <param name="HasDevice">False when Windows has no default output device (or it just disappeared).</param>
/// <param name="Volume">Master volume 0–1 of the default output device.</param>
public readonly record struct AudioState(bool HasDevice, float Volume, bool Muted)
{
    public static AudioState NoDevice { get; } = new(false, 0, false);
}

/// <summary>
/// Guide §3 contract: master volume and mute of the default output device (ADR 0022). Event-driven; never per-app volume.
/// Calls are safe from any thread; failures mean "no device" or "not changed", never exceptions.
/// </summary>
public interface IAudioService : IDisposable
{
    /// <summary>Raised (on any thread) when the volume, mute or default device changed — by the user, Windows or another app.</summary>
    event EventHandler? Changed;

    /// <summary>Connects to Windows audio. Returns <c>false</c> when the audio system is not available at all.</summary>
    bool Start();

    AudioState GetState();

    /// <param name="volume">0–1; clamped.</param>
    bool SetVolume(float volume);

    bool SetMuted(bool muted);
}
