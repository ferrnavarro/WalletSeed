using System.Text.Json;
using System.Text.Json.Serialization;
using CardStatement.Core.Models;

namespace CardStatement.App.Output;

public static class JsonWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
        },
    };

    public static async Task WriteAsync(StatementResult result, string path, CancellationToken ct = default)
    {
        var payload = new
        {
            statement = new
            {
                cardType = result.Statement.CardType,
                maskedAccount = result.Statement.MaskedAccount,
                period = new
                {
                    issueDate = result.Statement.Period.IssueDate.ToString("yyyy-MM-dd"),
                    cutoffDate = result.Statement.Period.CutoffDate.ToString("yyyy-MM-dd"),
                },
                pageCount = result.Statement.PageCount,
            },
            totals = new
            {
                income = result.TotalIncome,
                expense = result.TotalExpense,
            },
            reconciliationStatus = result.ReconciliationStatus,
            needsReviewCount = result.NeedsReviewCount,
            unmappedCards = result.UnmappedCards,
            records = result.Records.Select(r => new
            {
                date = r.Date.ToString("yyyy-MM-dd"),
                description = r.Description,
                direction = r.Direction,
                amount = r.Amount,
                categoryId = r.CategoryId,
                categoryName = r.CategoryName,
                labelId = r.LabelId,
                labelName = r.LabelName,
                cardLast4 = r.CardLast4,
                needsReview = r.NeedsReview,
                labelUnmapped = r.LabelUnmapped,
            }),
        };

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, payload, Options, ct).ConfigureAwait(false);
    }
}
