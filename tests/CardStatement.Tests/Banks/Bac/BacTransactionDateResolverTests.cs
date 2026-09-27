using System;
using CardStatement.Core.Models;
using CardStatement.Core.Banks.Bac;
using FluentAssertions;
using Xunit;

namespace CardStatement.Tests.Banks.Bac;

public class BacTransactionDateResolverTests
{
    [Fact]
    public void Parses_spanish_month_in_same_year_as_cutoff()
    {
        var period = new StatementPeriod(
            IssueDate: new DateOnly(2026, 5, 21),
            CutoffDate: new DateOnly(2026, 5, 18));
        var resolver = new BacTransactionDateResolver(period);

        resolver.ResolveTransactionDate("ABR/18").Should().Be(new DateOnly(2026, 4, 18));
        resolver.ResolveTransactionDate("MAY/05").Should().Be(new DateOnly(2026, 5, 5));
    }

    [Fact]
    public void Handles_dec_to_jan_rollover()
    {
        var period = new StatementPeriod(
            IssueDate: new DateOnly(2026, 1, 20),
            CutoffDate: new DateOnly(2026, 1, 17));
        var resolver = new BacTransactionDateResolver(period);

        resolver.ResolveTransactionDate("DIC/28").Should().Be(new DateOnly(2025, 12, 28));
        resolver.ResolveTransactionDate("ENE/05").Should().Be(new DateOnly(2026, 1, 5));
    }

    [Fact]
    public void Parses_posting_date_in_dd_mm_format()
    {
        var period = new StatementPeriod(
            new DateOnly(2026, 5, 21), new DateOnly(2026, 5, 18));
        var resolver = new BacTransactionDateResolver(period);

        resolver.ResolvePostingDate("19/04").Should().Be(new DateOnly(2026, 4, 19));
    }
}
