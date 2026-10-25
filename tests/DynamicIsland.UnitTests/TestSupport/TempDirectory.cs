namespace DynamicIsland.UnitTests.TestSupport;

/// <summary>Unique scratch folder under the system temp path, deleted on dispose.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DynamicIsland.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(string fileName) => System.IO.Path.Combine(Path, fileName);

    public string[] FileNames() =>
        Directory.GetFiles(Path).Select(System.IO.Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToArray();

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
