using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardStatement.Api.Tests.Fixtures;

public static class GroundTruth
{
    public static string Path { get; }
    public static ExpectedResult Data { get; }

    static GroundTruth()
    {
        var baseDir = AppContext.BaseDirectory;
        var relativePath = System.IO.Path.Combine(baseDir, "..", "..", "..", "..", "..", "result.json");
        Path = System.IO.Path.GetFullPath(relativePath);

        if (!File.Exists(Path))
        {
            throw new FileNotFoundException($"result.json ground truth file not found at: {Path}");
        }

        var json = File.ReadAllText(Path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        
        Data = JsonSerializer.Deserialize<ExpectedResult>(json, options)
            ?? throw new InvalidOperationException("Failed to deserialize result.json");
    }

    public sealed record ExpectedResult(
        ExpectedStatement Statement,
        ExpectedTotals Totals,
        string ReconciliationStatus,
        int NeedsReviewCount,
        IReadOnlyList<string> UnmappedCards,
        IReadOnlyList<ExpectedRecord> Records
    );

    public sealed record ExpectedStatement(
        string CardType,
        string MaskedAccount,
        ExpectedPeriod Period,
        int PageCount
    );

    public sealed record ExpectedPeriod(
        string IssueDate,
        string CutoffDate
    );

    public sealed record ExpectedTotals(
        decimal Income,
        decimal Expense
    );

    public sealed record ExpectedRecord(
        string Date,
        string Description,
        string Direction,
        decimal Amount,
        string? CategoryId,
        string? CategoryName,
        string? LabelId,
        string? LabelName,
        string CardLast4,
        bool NeedsReview,
        bool LabelUnmapped
    );
}
