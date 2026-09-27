using System;
using System.Collections.Generic;
using System.Linq;
using CardStatement.Core.Models;
using CardStatement.Core.Pdf;

namespace CardStatement.Core.Banks.Bac;

public sealed class BacTransactionTableLocator
{
    private readonly BacParsingOptions _options;

    public BacTransactionTableLocator(BacParsingOptions? options = null)
    {
        _options = options ?? new BacParsingOptions();
    }

    public IEnumerable<TableLayout> Locate(IReadOnlyList<PdfWord> words)
    {
        foreach (var pageGroup in words.GroupBy(w => w.PageNumber).OrderBy(g => g.Key))
        {
            if (TryLocate(pageGroup.Key, pageGroup, out var layout))
                yield return layout!;
        }
    }

    public bool TryLocate(int pageNumber, IEnumerable<PdfWord> pageWords, out TableLayout? layout)
    {
        layout = null;

        var concepto = pageWords.FirstOrDefault(w =>
            string.Equals(w.Text, "CONCEPTO", StringComparison.Ordinal));
        if (concepto is null)
            return false;

        var headerY = concepto.Y;
        var sameRow = pageWords
            .Where(w => Math.Abs(w.Y - headerY) <= _options.RowYTolerance + 1.0)
            .ToList();

        var cargos = sameRow.FirstOrDefault(w => string.Equals(w.Text, "CARGOS", StringComparison.Ordinal));
        var abonos = sameRow.FirstOrDefault(w => string.Equals(w.Text, "ABONOS", StringComparison.Ordinal));
        if (cargos is null || abonos is null)
            return false;

        var bands = new ColumnBands(
            TransactionDateX: 38.9,
            PostingDateX: 76.9,
            ReferenceX: 105.4,
            SequenceX: 148.2,
            DescriptionLeftX: 171.9,
            DescriptionRightX: 460.0,
            CargosAbonosSplitX: _options.CargosAbonosSplitX,
            PageRightX: 600.0);

        layout = new TableLayout(pageNumber, headerY, bands);
        return true;
    }
}
