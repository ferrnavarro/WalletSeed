using System.Globalization;
using CardStatement.Core.Models;

namespace CardStatement.Core.Parsing;

public sealed class TransactionDateResolver
{
    private readonly StatementPeriod _period;

    public TransactionDateResolver(StatementPeriod period)
    {
        _period = period;
    }

    public DateOnly ResolveTransactionDate(string token)
    {
        var parts = token.Split('/', 2);
        if (parts.Length != 2 || !SpanishMonths.TryGet(parts[0], out var month))
            throw new FormatException($"Unrecognized MMM/DD token: '{token}'");

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
            throw new FormatException($"Unrecognized day in token: '{token}'");

        var year = DeriveYear(month);
        return new DateOnly(year, month, day);
    }

    public DateOnly ResolvePostingDate(string token)
    {
        var parts = token.Split('/', 2);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var day) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var month))
            throw new FormatException($"Unrecognized DD/MM token: '{token}'");

        var year = DeriveYear(month);
        return new DateOnly(year, month, day);
    }

    private int DeriveYear(int month)
    {
        var cutoff = _period.CutoffDate;
        return month <= cutoff.Month ? cutoff.Year : cutoff.Year - 1;
    }
}
