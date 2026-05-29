using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CardStatement.Core.Abstractions;
using CardStatement.Core.Models;
using CardStatement.Core.Pdf;

namespace CardStatement.Core.Banks.Bac;

public sealed partial class BacStatementParser : IStatementParser
{
    [GeneratedRegex(@"^459378XXXXXX(?<last4>\d{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex SectionHeaderCardRegex();

    private readonly BacParsingOptions _options;
    private readonly BacTransactionTableLocator _locator;
    private readonly BacRowBuilder _rowBuilder;
    private readonly BacRowClassifier _classifier;

    public BacStatementParser(BacParsingOptions? options = null)
    {
        _options = options ?? new BacParsingOptions();
        _locator = new BacTransactionTableLocator(_options);
        _rowBuilder = new BacRowBuilder(_options);
        _classifier = new BacRowClassifier();
    }

    public Statement Parse(PdfDocumentWords words)
    {
        var metadata = new BacMetadataExtractor().Extract(words.Words);
        var dateResolver = new BacTransactionDateResolver(metadata.Period);

        var byPage = words.Words.GroupBy(w => w.PageNumber)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PdfWord>)g.ToList());

        var sections = new List<SectionAccumulator>();
        SectionAccumulator? current = null;
        decimal? statementCharges = null;
        decimal? statementCredits = null;

        foreach (var layout in _locator.Locate(words.Words))
        {
            var rowParser = new BacTransactionRowParser(dateResolver, layout.Bands);
            var rows = _rowBuilder.Build(layout, byPage[layout.PageNumber]);

            foreach (var row in rows)
            {
                var classified = _classifier.Classify(row);
                switch (classified.Kind)
                {
                    case BacClassifiedRowKind.SectionHeader:
                        current = OpenSection(row);
                        sections.Add(current);
                        break;

                    case BacClassifiedRowKind.Transaction:
                        if (current is null)
                            throw new InvalidOperationException(
                                $"Transaction row on p{row.PageNumber} before any section header.");
                        current.Transactions.Add(rowParser.Parse(row, current.CardLast4));
                        break;

                    case BacClassifiedRowKind.SectionSubtotal:
                        if (current is null) break;
                        ReadAmounts(row, layout.Bands, out var subC, out var subA);
                        current.PrintedSubtotalCharges = subC;
                        current.PrintedSubtotalCredits = subA;
                        break;

                    case BacClassifiedRowKind.StatementTotal:
                        ReadAmounts(row, layout.Bands, out var totC, out var totA);
                        statementCharges = totC;
                        statementCredits = totA;
                        break;
                }
            }
        }

        var built = sections.Select(s => new CardholderSection
        {
            CardLast4 = s.CardLast4,
            RawName = s.RawName,
            Transactions = s.Transactions,
            PrintedSubtotalCharges = s.PrintedSubtotalCharges,
            PrintedSubtotalCredits = s.PrintedSubtotalCredits,
        }).ToList();

        return new Statement
        {
            CardType = metadata.CardType,
            MaskedAccount = metadata.MaskedAccount,
            Period = metadata.Period,
            PageCount = words.PageCount,
            Sections = built,
            PrintedTotalCharges = statementCharges,
            PrintedTotalCredits = statementCredits,
        };
    }

    private static SectionAccumulator OpenSection(BacTableRow row)
    {
        var first = row.Words[0].Text;
        var m = SectionHeaderCardRegex().Match(first);
        if (!m.Success)
            throw new InvalidOperationException($"Section header did not match: '{first}'");

        var last4 = m.Groups["last4"].Value;
        var nameWords = row.Words
            .Skip(1)
            .SkipWhile(w => w.Text == "»»»")
            .Select(w => w.Text);
        var name = string.Join(" ", nameWords).Trim();
        return new SectionAccumulator(last4, name);
    }

    private static void ReadAmounts(BacTableRow row, ColumnBands bands, out decimal? cargos, out decimal? abonos)
    {
        cargos = null;
        abonos = null;

        for (var i = 0; i < row.Words.Count; i++)
        {
            var w = row.Words[i];
            if (!BacAmountParser.TryParse(w.Text, out var value)) continue;
            if (w.X < bands.CargosAbonosSplitX)
                cargos = value;
            else
                abonos = value;
        }
    }

    private sealed class SectionAccumulator
    {
        public SectionAccumulator(string cardLast4, string rawName)
        {
            CardLast4 = cardLast4;
            RawName = rawName;
        }

        public string CardLast4 { get; }
        public string RawName { get; }
        public List<Transaction> Transactions { get; } = [];
        public decimal? PrintedSubtotalCharges { get; set; }
        public decimal? PrintedSubtotalCredits { get; set; }
    }
}
