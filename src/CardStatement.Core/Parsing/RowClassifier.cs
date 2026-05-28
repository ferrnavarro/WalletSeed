using System.Text.RegularExpressions;
using CardStatement.Core.Models;
using CardStatement.Core.Pdf;

namespace CardStatement.Core.Parsing;

public enum ClassifiedRowKind
{
    SectionHeader,
    Transaction,
    SectionSubtotal,
    StatementTotal,
    Noise,
}

public sealed record ClassifiedRow(ClassifiedRowKind Kind, TableRow Row);

public sealed partial class RowClassifier
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

    public ClassifiedRow Classify(TableRow row)
    {
        if (row.Words.Count == 0)
            return new ClassifiedRow(ClassifiedRowKind.Noise, row);

        var first = row.Words[0].Text;

        if (SectionHeaderCardRegex().IsMatch(first))
            return new ClassifiedRow(ClassifiedRowKind.SectionHeader, row);

        if (first is "SUBTOTAL.:")
            return new ClassifiedRow(ClassifiedRowKind.SectionSubtotal, row);

        if (first == "TOTAL" && row.Words.Count > 1 && row.Words[1].Text == "...:")
            return new ClassifiedRow(ClassifiedRowKind.StatementTotal, row);

        if (NoiseStarters.Contains(first))
            return new ClassifiedRow(ClassifiedRowKind.Noise, row);

        if (TransactionDateRegex().IsMatch(first))
            return new ClassifiedRow(ClassifiedRowKind.Transaction, row);

        return new ClassifiedRow(ClassifiedRowKind.Noise, row);
    }
}
