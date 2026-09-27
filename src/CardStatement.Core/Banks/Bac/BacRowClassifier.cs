using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CardStatement.Core.Models;

namespace CardStatement.Core.Banks.Bac;

public enum BacClassifiedRowKind
{
    SectionHeader,
    Transaction,
    SectionSubtotal,
    StatementTotal,
    Noise,
}

public sealed record BacClassifiedRow(BacClassifiedRowKind Kind, BacTableRow Row);

public sealed partial class BacRowClassifier
{
    [GeneratedRegex(@"^459378XXXXXX(?<last4>\d{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex SectionHeaderCardRegex();

    [GeneratedRegex(@"^[A-Z]{3}/\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex TransactionDateRegex();

    private static readonly HashSet<string> NoiseStarters = new(StringComparer.Ordinal)
    {
        "TRANSACCION",
        "PUNTOS",
        "ASIGNADOS:",
        "BONIFICACION",
        "RETENCION",
    };

    public BacClassifiedRow Classify(BacTableRow row)
    {
        if (row.Words.Count == 0)
            return new BacClassifiedRow(BacClassifiedRowKind.Noise, row);

        var first = row.Words[0].Text;

        if (SectionHeaderCardRegex().IsMatch(first))
            return new BacClassifiedRow(BacClassifiedRowKind.SectionHeader, row);

        if (first is "SUBTOTAL.:")
            return new BacClassifiedRow(BacClassifiedRowKind.SectionSubtotal, row);

        if (first == "TOTAL" && row.Words.Count > 1 && row.Words[1].Text == "...:")
            return new BacClassifiedRow(BacClassifiedRowKind.StatementTotal, row);

        if (NoiseStarters.Contains(first))
            return new BacClassifiedRow(BacClassifiedRowKind.Noise, row);

        if (TransactionDateRegex().IsMatch(first))
            return new BacClassifiedRow(BacClassifiedRowKind.Transaction, row);

        return new BacClassifiedRow(BacClassifiedRowKind.Noise, row);
    }
}
