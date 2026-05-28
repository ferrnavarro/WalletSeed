using CardStatement.Core.Abstractions;
using CardStatement.Core.Categorization;
using CardStatement.Core.Models;
using FluentAssertions;

namespace CardStatement.Tests.Categorization;

public class LlmCategorizerTests
{
    private static readonly Guid GroceriesId = new("041e43d7-6a9c-4acc-b877-29ceb0811fe4");
    private static readonly Guid DebtId = new("10c12b30-bf94-4b58-bac8-3e2e4528feb6");
    private static readonly Guid LoanId = new("3aea0a15-d1b5-46fd-9965-98868dd410ca");
    private static readonly Guid RefundsId = new("0ba25396-1fd3-477d-9b73-100a4229942b");
    private static readonly Guid FallbackId = new("40b565bb-d9cc-430a-a4ef-0c8649b636ab");

    private static readonly Category[] Taxonomy =
    [
        new(GroceriesId, "Groceries"),
        new(DebtId, "Debt"),
        new(LoanId, "Loan, interests"),
        new(RefundsId, "Refunds (tax, purchase)"),
        new(FallbackId, "Automatic bank statements reading"),
    ];

    [Fact]
    public async Task Payment_row_uses_fixed_payment_category_and_bypasses_llm()
    {
        var llm = new RecordingLlmClient();
        var categorizer = MakeCategorizer(llm);
        var payment = MakeTx(RowType.Payment, Direction.Income, 802.01m);

        var result = await categorizer.CategorizeAsync(new[] { payment });

        result.Should().ContainSingle();
        result[0].CategoryId.Should().Be(DebtId);
        result[0].CategoryName.Should().Be("Debt");
        result[0].NeedsReview.Should().BeFalse();
        llm.CalledTimes.Should().Be(0);
    }

    [Fact]
    public async Task Financing_charge_uses_loan_interests()
    {
        var llm = new RecordingLlmClient();
        var categorizer = MakeCategorizer(llm);
        var financing = MakeTx(RowType.Financing, Direction.Expense, 39.00m);

        var result = await categorizer.CategorizeAsync(new[] { financing });

        result[0].CategoryId.Should().Be(LoanId);
        llm.CalledTimes.Should().Be(0);
    }

    [Fact]
    public async Task Financing_reversal_uses_refunds_category()
    {
        var llm = new RecordingLlmClient();
        var categorizer = MakeCategorizer(llm);
        var reversal = MakeTx(RowType.Financing, Direction.Income, 75.00m);

        var result = await categorizer.CategorizeAsync(new[] { reversal });

        result[0].CategoryId.Should().Be(RefundsId);
    }

    [Fact]
    public async Task Purchase_goes_to_llm_and_uses_returned_id()
    {
        var llm = new RecordingLlmClient(itemId => GroceriesId);
        var categorizer = MakeCategorizer(llm);
        var purchase = MakeTx(RowType.Purchase, Direction.Expense, 7.42m, desc: "SELECTOS MASFERRER");

        var result = await categorizer.CategorizeAsync(new[] { purchase });

        result[0].CategoryId.Should().Be(GroceriesId);
        result[0].NeedsReview.Should().BeFalse();
        llm.CalledTimes.Should().Be(1);
    }

    [Fact]
    public async Task Invented_category_id_is_rejected_and_falls_back_with_needs_review()
    {
        var bogus = Guid.NewGuid();
        var llm = new RecordingLlmClient(_ => bogus);
        var categorizer = MakeCategorizer(llm);
        var purchase = MakeTx(RowType.Purchase, Direction.Expense, 7.42m, desc: "SELECTOS MASFERRER");

        var result = await categorizer.CategorizeAsync(new[] { purchase });

        result[0].CategoryId.Should().Be(FallbackId);
        result[0].NeedsReview.Should().BeTrue();
    }

    private static LlmCategorizer MakeCategorizer(ILlmClient llm)
    {
        var options = new CategorizationOptions
        {
            BatchSize = 30,
            FallbackCategoryId = FallbackId,
            FlagFallbackAsNeedsReview = true,
        };
        var fixedResolver = new FixedCategoryResolver(Taxonomy, options.FixedCategoryNames, options.FallbackCategoryId);
        return new LlmCategorizer(llm, fixedResolver, Taxonomy, options);
    }

    private static Transaction MakeTx(RowType type, Direction direction, decimal amount, string desc = "x") => new()
    {
        TransactionDate = new DateOnly(2026, 5, 1),
        PostingDate = new DateOnly(2026, 5, 2),
        ReferenceNumber = "0",
        SequenceCode = type switch
        {
            RowType.Payment => "P001",
            RowType.Financing => "X001",
            _ => "C001",
        },
        RowType = type,
        RawDescription = desc,
        Amount = amount,
        Direction = direction,
        CardLast4 = "2533",
    };

    private sealed class RecordingLlmClient : ILlmClient
    {
        private readonly Func<string, Guid?> _picker;
        public int CalledTimes { get; private set; }

        public RecordingLlmClient(Func<string, Guid?>? picker = null)
        {
            _picker = picker ?? (_ => null);
        }

        public Task<IReadOnlyList<LlmCategoryChoice>> CategorizeBatchAsync(
            IReadOnlyList<LlmCategorizationItem> items,
            IReadOnlyList<Category> allowedCategories,
            CancellationToken ct = default)
        {
            CalledTimes++;
            IReadOnlyList<LlmCategoryChoice> result = items
                .Select(i => new LlmCategoryChoice(i.ItemId, _picker(i.ItemId)))
                .ToList();
            return Task.FromResult(result);
        }
    }
}
