using CardStatement.Core.Models;

namespace CardStatement.App.Output;

public sealed class OutputWriter
{
    public async Task WriteAsync(StatementResult result, string? jsonPath, string? csvPath, CancellationToken ct = default)
    {
        ConsoleSummaryPrinter.Print(result);

        if (jsonPath is not null)
        {
            await JsonWriter.WriteAsync(result, jsonPath, ct).ConfigureAwait(false);
            Console.WriteLine();
            Console.WriteLine($"JSON written : {jsonPath}");
        }

        if (csvPath is not null)
        {
            await CsvWriter.WriteAsync(result, csvPath, ct).ConfigureAwait(false);
            Console.WriteLine($"CSV  written : {csvPath}");
        }
    }
}
