namespace CardStatement.Core.Categorization;

public sealed class CategorizationOptions
{
    public string Provider { get; set; } = "stub";
    public int BatchSize { get; set; } = 30;
    public Guid? FallbackCategoryId { get; set; }
    public bool FlagFallbackAsNeedsReview { get; set; } = true;
    public FixedCategoryNamesOptions FixedCategoryNames { get; set; } = new();
    public OpenAiOptions OpenAi { get; set; } = new();
}

public sealed class FixedCategoryNamesOptions
{
    public string Payment { get; set; } = "Debt";
    public string FinancingCharge { get; set; } = "Loan, interests";
    public string FinancingReversal { get; set; } = "Refunds (tax, purchase)";
}

public sealed class OpenAiOptions
{
    public string Model { get; set; } = "gpt-4.1-mini";
    public string ApiKey { get; set; } = "";
    public string? BaseUrl { get; set; }
    public bool UseJsonMode { get; set; } = true;
}
