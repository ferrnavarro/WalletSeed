using System.IO;

namespace CardStatement.Api.Tests.Fixtures;

public static class SamplePdf
{
    public static string Path { get; }

    static SamplePdf()
    {
        // Resolve absolute path to the samples folder relative to the test assembly location
        var baseDir = AppContext.BaseDirectory;
        // From tests/CardStatement.Api.Tests/bin/Debug/net10.0/, we go up 5 levels to root, then samples/
        var relativePath = System.IO.Path.Combine(baseDir, "..", "..", "..", "..", "..", "samples", "final5140_45178439_316493_0.pdf");
        Path = System.IO.Path.GetFullPath(relativePath);

        if (!File.Exists(Path))
        {
            throw new FileNotFoundException($"Sample PDF file not found at: {Path}");
        }
    }

    public static FileStream OpenRead() => File.OpenRead(Path);
}
