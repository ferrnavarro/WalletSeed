using System;
using System.Linq;
using System.Text.RegularExpressions;
using CardStatement.Core.Models;
using CardStatement.Core.Abstractions;

namespace CardStatement.Core.Banks.Bac;

public sealed partial class BacDetector
{
    [GeneratedRegex(@"^459378XXXXXX\d{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex BinRegex();

    public BankDetection Detect(PdfDocumentWords words)
    {
        if (words == null || words.Words.Count == 0)
            return BankDetection.NoMatch();

        var hasBin = words.Words.Any(w => w.PageNumber == 1 && BinRegex().IsMatch(w.Text));

        var hasHeader = new BacTransactionTableLocator().Locate(words.Words).Any();

        if (hasBin && hasHeader)
        {
            return BankDetection.Match(BankDetection.HighConfidence, "BIN 459378 + CONCEPTO/CARGOS/ABONOS table header found");
        }

        if (hasHeader)
        {
            return BankDetection.Match(BankDetection.MediumConfidence, "CONCEPTO/CARGOS/ABONOS table header found without BIN");
        }

        return BankDetection.NoMatch();
    }
}
