namespace CardStatement.Api.Wallet;

public interface IWalletApiClient
{
    Task<IReadOnlyList<WalletAccount>> ListAccountsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<WalletCategory>> ListCategoriesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<WalletRecord>> ListRecordsAsync(string accountId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<WalletLabel>> ListLabelsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<WalletCreateOutcome>> CreateRecordsAsync(IReadOnlyList<WalletCreateRequest> rows, CancellationToken ct = default);
}
