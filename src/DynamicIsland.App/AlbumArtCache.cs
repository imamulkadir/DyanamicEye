using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.App;

/// <summary>
/// Decodes album art off the UI thread (ADR 0020): bytes are already size-capped by the widget; decoding is scaled down
/// to <see cref="DecodeSize"/> pixels and frozen. Holds only the current image. Images that fail to decode are shown
/// as no art; nothing about them is logged.
/// </summary>
public sealed class AlbumArtCache
{
    public const int DecodeSize = 96;

    private readonly Lock _gate = new();
    private string? _key;
    private ImageSource? _image;

    /// <summary>Raised on a background thread when a requested image finished decoding.</summary>
    public event EventHandler? Decoded;

    /// <summary>The decoded image for <paramref name="key"/>, or <c>null</c> while it decodes (a redraw follows) or when it is unusable.</summary>
    public ImageSource? Get(string? key, byte[]? bytes)
    {
        if (key is null || bytes is null)
        {
            return null;
        }

        lock (_gate)
        {
            if (key == _key)
            {
                return _image; // Null while still decoding.
            }

            _key = key;
            _image = null;
        }

        _ = Task.Run(() => DecodeAndPublish(key, bytes));
        return null;
    }

    private void DecodeAndPublish(string key, byte[] bytes)
    {
        var image = Decode(bytes);
        lock (_gate)
        {
            if (key != _key)
            {
                return; // A newer track arrived meanwhile.
            }

            _image = image;
        }

        Decoded?.Invoke(this, EventArgs.Empty);
    }

    private static BitmapImage? Decode(byte[] bytes)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = DecodeSize;
            image.StreamSource = new MemoryStream(bytes, writable: false);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }
}
