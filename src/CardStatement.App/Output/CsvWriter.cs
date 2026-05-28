using System.Globalization;
using System.Text;
using CardStatement.Core.Models;

namespace CardStatement.App.Output;

public static class CsvWriter
{
    public static async Task WriteAsync(StatementResult result, string path, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("date,description,direction,amount,categoryId,categoryName,labelId,labelName,cardLast4,needsReview,labelUnmapped");
        foreach (var r in result.Records)
        {
            sb.Append(r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(Quote(r.Description)).Append(',');
            sb.Append(r.Direction.ToString().ToLowerInvariant()).Append(',');
            sb.Append(r.Amount.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.CategoryId?.ToString()).Append(',');
            sb.Append(Quote(r.CategoryName ?? "")).Append(',');
            sb.Append(r.LabelId?.ToString()).Append(',');
            sb.Append(Quote(r.LabelName ?? "")).Append(',');
            sb.Append(r.CardLast4).Append(',');
            sb.Append(r.NeedsReview).Append(',');
            sb.AppendLine(r.LabelUnmapped.ToString());
        }
        await File.WriteAllTextAsync(path, sb.ToString(), ct).ConfigureAwait(false);
    }

    private static string Quote(string s)
    {
        if (s.IndexOfAny(['"', ',', '\n', '\r']) < 0) return s;
        var escaped = s.Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }
}
