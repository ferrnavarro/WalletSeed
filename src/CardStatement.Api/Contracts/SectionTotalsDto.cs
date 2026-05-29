namespace CardStatement.Api.Contracts;

public sealed record SectionTotalsDto(
    decimal ComputedCharges,
    decimal ComputedCredits,
    decimal? PrintedCharges,
    decimal? PrintedCredits
);
