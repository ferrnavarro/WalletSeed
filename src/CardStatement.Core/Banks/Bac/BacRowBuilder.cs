using System;
using System.Collections.Generic;
using System.Linq;
using CardStatement.Core.Models;
using CardStatement.Core.Pdf;

namespace CardStatement.Core.Banks.Bac;

public sealed record BacTableRow(int PageNumber, double Y, IReadOnlyList<PdfWord> Words);

public sealed class BacRowBuilder
{
    private readonly BacParsingOptions _options;

    public BacRowBuilder(BacParsingOptions? options = null)
    {
        _options = options ?? new BacParsingOptions();
    }

    public IReadOnlyList<BacTableRow> Build(TableLayout layout, IEnumerable<PdfWord> pageWords)
    {
        var topY = layout.HeaderY - _options.RowYTolerance - 1.0;
        var bottomY = _options.PageFooterCutoffY;

        var tableWords = pageWords
            .Where(w => w.PageNumber == layout.PageNumber)
            .Where(w => w.Y < topY && w.Y > bottomY)
            .OrderByDescending(w => w.Y)
            .ToList();

        var rows = new List<BacTableRow>();
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

    private static BacTableRow MakeRow(int page, double y, List<PdfWord> words)
    {
        var ordered = words.OrderBy(w => w.X).ToList();
        return new BacTableRow(page, y, ordered);
    }
}
