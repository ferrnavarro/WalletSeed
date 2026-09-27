namespace CardStatement.Api.Wallet.Contracts;

public sealed record SubmitResponse(IReadOnlyList<SubmitOutcomeDto> Outcomes);

public sealed record SubmitOutcomeDto(int Index, bool Ok, string? WalletRecordId, string? ErrorMessage);
