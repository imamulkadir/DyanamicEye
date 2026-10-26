using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.App;
using DynamicIsland.Core.Shell;
using DynamicIsland.TestSupport;

namespace DynamicIsland.IntegrationTests;

/// <summary>
/// Visual regression (guide P02-05): renders the real <see cref="NotchView"/> offscreen at 96 DPI and compares it with
/// committed reference images of <b>our own</b> rendering (never third-party artwork). Offscreen rendering is independent
/// of the desktop behind the window and of display scaling. To accept an intentional design change, run the tests with
/// <c>DYNAMICISLAND_UPDATE_VISUAL_REFERENCES=1</c> and review the new PNGs in the diff.
/// </summary>
public sealed class NotchVisualTests : IDisposable
{
    private const string UpdateVariable = "DYNAMICISLAND_UPDATE_VISUAL_REFERENCES";
    private const int ChannelTolerance = 24;
    private const double MaxDifferingPixelRatio = 0.01;

    private readonly StaDispatcherThread _ui = new();

    public void Dispose() => _ui.Dispose();

    public static TheoryData<IslandState, string> States => new()
    {
        { IslandState.Collapsed, "Dark" },
        { IslandState.Peek, "Dark" },
        { IslandState.Expanded, "Dark" },
        { IslandState.Pinned, "Dark" },
        { IslandState.Collapsed, "Light" }, // ADR 0026
        { IslandState.Pinned, "Light" },
    };

    [Theory]
    [MemberData(nameof(States))]
    public void Notch_matches_its_reference_rendering(IslandState state, string theme)
    {
        var name = $"notch-{state.ToString().ToLowerInvariant()}{(theme == "Dark" ? string.Empty : "-" + theme.ToLowerInvariant())}.png";
        var actual = _ui.Invoke(() => Render(state, theme == "Light" ? IslandTheme.Light : IslandTheme.Dark));

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Save(actual, Path.Combine(ReferenceSourceFolder(), name));
            return;
        }

        var referencePath = Path.Combine(AppContext.BaseDirectory, "References", name);
        Assert.True(File.Exists(referencePath), $"Missing reference {name}; generate it with {UpdateVariable}=1.");
        var expected = Load(referencePath);

        var (differing, total) = Compare(expected, actual);
        if ((double)differing / total > MaxDifferingPixelRatio)
        {
            var outputFolder = Path.Combine(AppContext.BaseDirectory, "VisualDiffs");
            Directory.CreateDirectory(outputFolder);
            Save(actual, Path.Combine(outputFolder, "actual-" + name));
            Assert.Fail($"{name}: {differing} of {total} pixels differ (> {MaxDifferingPixelRatio:P0}). Actual saved to {outputFolder}.");
        }
    }

    private static RenderTargetBitmap Render(IslandState state, IslandPalette palette)
    {
        var size = IslandLayout.For(state)!.Value;
        var view = new NotchView { Width = size.Width, Height = size.Height };
        view.ApplyTheme(palette);
        view.ApplyVisualState(state, size.Height, scale: 1.0);
        view.SetClock("2:42 PM"); // Fixed text: the reference must not depend on the time of the run.

        // The notch on a mid-grey backdrop, so the outline (shoulders, corners) is part of the comparison.
        var backdrop = new System.Windows.Controls.Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            Child = view,
            Width = size.Width,
            Height = size.Height,
        };
        backdrop.Measure(new Size(size.Width, size.Height));
        backdrop.Arrange(new Rect(0, 0, size.Width, size.Height));
        backdrop.UpdateLayout();

        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(backdrop);
        bitmap.Freeze();
        return bitmap;
    }

    private static (int Differing, int Total) Compare(BitmapSource expected, BitmapSource actual)
    {
        Assert.Equal(expected.PixelWidth, actual.PixelWidth);
        Assert.Equal(expected.PixelHeight, actual.PixelHeight);

        var a = Pixels(expected);
        var b = Pixels(actual);
        var differing = 0;
        for (var i = 0; i < a.Length; i += 4)
        {
            if (Math.Abs(a[i] - b[i]) > ChannelTolerance || Math.Abs(a[i + 1] - b[i + 1]) > ChannelTolerance
                || Math.Abs(a[i + 2] - b[i + 2]) > ChannelTolerance || Math.Abs(a[i + 3] - b[i + 3]) > ChannelTolerance)
            {
                differing++;
            }
        }

        return (differing, a.Length / 4);
    }

    private static byte[] Pixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static BitmapFrame Load(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string ReferenceSourceFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "DynamicIsland.IntegrationTests", "References");
            if (File.Exists(Path.Combine(dir.FullName, "global.json")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
