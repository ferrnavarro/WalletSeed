using CardStatement.Core.Models;

namespace CardStatement.Core.Pdf;

public sealed record TableRow(int PageNumber, double Y, IReadOnlyList<PdfWord> Words);

public sealed class RowBuilder
{
    private readonly ParsingOptions _options;

    public RowBuilder(ParsingOptions? options = null)
    {
        _options = options ?? new ParsingOptions();
    }

    public IReadOnlyList<TableRow> Build(TableLayout layout, IEnumerable<PdfWord> pageWords)
    {
        var topY = layout.HeaderY - _options.RowYTolerance - 1.0;
        var bottomY = _options.PageFooterCutoffY;

        var tableWords = pageWords
            .Where(w => w.PageNumber == layout.PageNumber)
            .Where(w => w.Y < topY && w.Y > bottomY)
            .OrderByDescending(w => w.Y)
            .ToList();

        var rows = new List<TableRow>();
        var current = new List<PdfWord>();
        double? currentY = null;

        foreach (var word in tableWords)
        {
            if (currentY is null || Math.Abs(word.Y - currentY.Value) <= _options.RowYTolerance)
            {
                current.Add(word);
                currentY ??= word.Y;
            }
            else
            {
                rows.Add(MakeRow(layout.PageNumber, currentY.Value, current));
                current = [word];
                currentY = word.Y;
            }
        }

        if (current.Count > 0 && currentY is not null)
            rows.Add(MakeRow(layout.PageNumber, currentY.Value, current));

        return rows;
    }

    private static TableRow MakeRow(int page, double y, List<PdfWord> words)
    {
        var ordered = words.OrderBy(w => w.X).ToList();
        return new TableRow(page, y, ordered);
    }
}
