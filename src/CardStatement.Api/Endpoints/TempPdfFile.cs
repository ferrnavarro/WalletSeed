using Microsoft.AspNetCore.Http;

namespace CardStatement.Api.Endpoints;

public sealed class TempPdfFile : IDisposable
{
    public string Path { get; }

    public TempPdfFile(IFormFile file)
    {
        Path = System.IO.Path.GetTempFileName();
        using var stream = System.IO.File.Create(Path);
        file.CopyTo(stream);
    }

    public void Dispose()
    {
        try
        {
            if (System.IO.File.Exists(Path))
            {
                System.IO.File.Delete(Path);
            }
        }
        catch
        {
            // Ignore failure to delete temp file
        }
    }
}
