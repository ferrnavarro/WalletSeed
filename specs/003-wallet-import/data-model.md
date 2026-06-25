# Data Model: Wallet Import from BAC PDF Statements

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Date**: 2026-06-25

This file is the concrete shape every type introduced by this feature takes. All types live under `src/CardStatement.Api/Wallet/…` (or under `src/CardStatement.Api/Wallet/Contracts/` for wire DTOs). Nothing here is added to `CardStatement.Core`.

> Convention: All wire DTOs are `sealed record`s. Internal types (`WalletApiException`, `DuplicateMatcher`, `WalletImportService`, etc.) are `internal sealed` unless they need to be public for testing.

---

## 1. Configuration types

### `WalletOptions`

```csharp
namespace CardStatement.Api.Wallet;

public sealed class WalletOptions
{
    public string? BaseUrl { get; set; }
    public string? Jwt { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public Dictionary<string, string> LabelMapping { get; set; } = new();
}
```

Bound from `appsettings.json` section `Wallet`. Env-var overrides per .NET convention: `Wallet__Jwt`, `Wallet__BaseUrl`, `Wallet__TimeoutSeconds`. `LabelMapping` env-vars are awkward for keys containing spaces; `appsettings.json` is the recommended channel.

**Invariants** (validated in `WalletApiClient.EnsureConfigured()`):
- `BaseUrl` MUST be non-empty and parse as an absolute `Uri` — else: `WalletApiException(NotConfigured)`.
- `Jwt` MUST be non-empty — else: `WalletApiException(NotConfigured)`.
- `TimeoutSeconds` MUST be `>= 1` — else: clamped to `30` with a warning log.

**Backs**: FR-022 (JWT lives only in backend config), FR-024 (missing JWT does not gate startup).

---

### Sample `appsettings.json` block

```json
{
  "Wallet": {
    "BaseUrl": "https://api.wallet.example.com",
    "Jwt": "eyJhbGciOiJI...",
    "TimeoutSeconds": 30,
    "LabelMapping": {
      "FERNANDO MAGAÑA":       "lbl_main_cardholder_xxx",
      "MARIA GARCÍA":          "lbl_secondary_cardholder_xxx"
    }
  }
}
```

---

## 2. Outbound Wallet API client

### `IWalletApiClient`

```csharp
namespace CardStatement.Api.Wallet;

public interface IWalletApiClient
{
    Task<IReadOnlyList<WalletAccount>> ListAccountsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<WalletCategory>> ListCategoriesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<WalletRecord>> ListRecordsAsync(string accountId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<WalletCreateOutcome>> CreateRecordsAsync(IReadOnlyList<WalletCreateRequest> rows, CancellationToken ct = default);
}
```

### `WalletAccount` / `WalletCategory` (internal mirrors of the Wallet API entities, kept narrow)

```csharp
public sealed record WalletAccount(
    string Id,
    string Name,
    string CurrencyCode,
    string AccountType,
    bool Archived
);

public sealed record WalletCategory(
    string Id,
    string Name,
    string? Color
);
```

### `WalletRecord`

```csharp
public sealed record WalletRecord(
    string Id,
    DateOnly RecordDate,
    decimal SignedAmount,           // Wallet's amount.value, already signed
    string CurrencyCode,
    string? Note,
    string? CounterParty,
    string? CategoryName
);
```

`SignedAmount` is the value Wallet sends back on `amount.value` — already negative for expense / positive for income, by Wallet's own contract.

### `WalletCreateRequest`

```csharp
public sealed record WalletCreateRequest(
    string AccountId,
    DateTimeOffset RecordDate,      // ISO 8601 datetime — Wallet accepts datetime, not just date
    decimal SignedAmount,
    string? CurrencyCode,           // omitted ⇒ Wallet defaults to account's currency
    string PaymentType,             // always "credit_card" in this iteration
    string CategoryId,
    IReadOnlyList<string> LabelIds,
    string? Note,
    string? CounterParty
);
```

### `WalletCreateOutcome`

```csharp
public sealed record WalletCreateOutcome(
    int InputIndex,                 // 0-based index in the chunk
    bool Success,
    string? Id,                     // present when Success
    string? Error                   // present when !Success
);
```

### `WalletApiException`

```csharp
public enum WalletApiErrorKind
{
    NotConfigured,                  // local: BaseUrl or Jwt missing
    CredentialsInvalid,             // upstream: 401/403
    Unavailable,                    // upstream: timeout, network, 5xx
    Rejected                        // upstream: 4xx other than 401/403
}

public sealed class WalletApiException : Exception
{
    public WalletApiErrorKind Kind { get; }
    public int? HttpStatus { get; }
    public string? BodyExcerpt { get; }   // first 256 chars of upstream body for diagnostics; logged, not shown to caller

    public WalletApiException(WalletApiErrorKind kind, string message, int? httpStatus = null, string? bodyExcerpt = null)
        : base(message) { Kind = kind; HttpStatus = httpStatus; BodyExcerpt = bodyExcerpt; }
}
```

**Backs**: FR-008 (distinct wallet error states), FR-020 (401/403 surfaces as one banner), FR-024 (NotConfigured separate from CredentialsInvalid).

---

## 3. Duplicate matcher

### `DuplicateMatcher` (pure static)

```csharp
internal static class DuplicateMatcher
{
    /// <summary>
    /// Pairs PDF rows to Wallet records by the FR-009 rule:
    ///   |date - recordDate| <= 2 days
    ///   AND signedAmount equal (decimal ==)
    ///   AND currency equal (ordinal)
    ///   AND same sign (both expense, or both income)
    /// </summary>
    /// <returns>
    /// Dictionary keyed by pdfRow.Index → sorted list of matched wallet record ids.
    /// Output is deterministic: keys ascending, value list lexicographic.
    /// </returns>
    public static IReadOnlyDictionary<int, IReadOnlyList<string>> Match(
        IReadOnlyList<PdfRowInternal> pdfRows,
        IReadOnlyList<WalletRecord> walletRecords);
}

internal sealed record PdfRowInternal(
    int Index,
    DateOnly Date,
    decimal SignedAmount,
    string Currency,
    string Description,
    string? CounterParty,
    string CardholderSectionRawName,
    string CardLast4
);
```

**Invariants**:
- For two consecutive calls with the same arguments, `Match` returns dictionaries whose `(key, value-list)` enumerations are byte-identical. Tests assert this directly.
- `pdfRows[i].Index` is its position in the input list. Callers preserve indices end-to-end.

**Backs**: FR-009, FR-025.

---

## 4. Label mapping resolver

### `LabelMappingResolver`

```csharp
internal sealed class LabelMappingResolver
{
    public LabelMappingResolver(IOptions<WalletOptions> options);

    /// <summary>Returns labelIds for the given cardholder RawName. Case-insensitive lookup.</summary>
    public IReadOnlyList<string> Resolve(string cardholderRawName);

    /// <summary>Returns the distinct set of cardholder names that have no mapping.</summary>
    public IReadOnlyList<string> FindUnmapped(IEnumerable<string> cardholderRawNames);
}
```

The current iteration treats each cardholder name as mapping to **zero or one** label id (the value type in `WalletOptions.LabelMapping` is `string`, not `List<string>`). If a future iteration needs multiple labels per cardholder, the options type changes; `Resolve` stays the same shape.

**Backs**: FR-017.

---

## 5. Import service (orchestrator)

### `WalletImportService`

```csharp
internal sealed class WalletImportService
{
    public WalletImportService(
        IPdfExtractor pdfExtractor,
        IBankResolver bankResolver,
        IReconciler reconciler,
        IWalletApiClient walletClient,
        LabelMappingResolver labelMapping,
        IOptions<WalletOptions> options,
        ILogger<WalletImportService> logger);

    public Task<CompareResponse> CompareAsync(string tempPdfPath, string accountId, CancellationToken ct = default);
    public Task<SubmitResponse> SubmitAsync(SubmitRequest req, CancellationToken ct = default);
}
```

`CompareAsync` flow:

1. Extract PDF words via `IPdfExtractor`. Empty → throw `NoTextExtractableException` (existing).
2. Resolve bank + parse statement via `IBankResolver`. No sections → throw `UnrecognizedLayoutException` (existing).
3. Reconcile via `IReconciler` (existing).
4. Compute statement window: `[ issueDate - 5d, cutoffDate + 5d ]` (research §7).
5. Concurrently fetch via the Wallet client (one Task.WhenAll): `account by id` (filter from `ListAccountsAsync`), `categories`, `records in window`.
6. Project PDF transactions → `PdfRowInternal[]` (one row per `Transaction` across all `Sections`, in original document order; assigned `Index = 0..N-1`).
7. Run `DuplicateMatcher.Match(...)`.
8. Project `PdfRowInternal[]` + `WalletRecord[]` + pairings → `CompareResponse` (see §6).

`SubmitAsync` flow:

1. Resolve labels per row (`LabelMappingResolver`).
2. Build `WalletCreateRequest` per row: `paymentType="credit_card"`, `accountId=req.AccountId`, signed amount, currency (account currency if not supplied per row), category, labels.
3. Chunk by 50.
4. For each chunk: `await walletClient.CreateRecordsAsync(chunk, ct)`.
5. Accumulate outcomes, preserving the input row indices.
6. Return `SubmitResponse`.

**Determinism**: All projections sort by `(PdfRowInternal.Index)` or `(WalletRecord.RecordDate, WalletRecord.Id)`. The order in `CompareResponse.pdfRows` matches the document order; the order in `CompareResponse.walletRows` is `(recordDate ASC, id ASC)`.

**Backs**: FR-005, FR-007, FR-009, FR-011, FR-014, FR-017, FR-018, FR-019, FR-025.

---

## 6. Wire DTOs (new endpoints)

All under `CardStatement.Api.Wallet.Contracts`. Field names below are the JSON names after `camelCase` policy.

### `CompareResponse`

```csharp
public sealed record CompareResponse(
    StatementWindowDto Window,
    WalletAccountDto Account,
    IReadOnlyList<WalletCategoryDto> Categories,    // sent once so the frontend can populate per-row dropdowns
    IReadOnlyList<PdfRowDto> PdfRows,
    IReadOnlyList<WalletRowDto> WalletRows,
    IReadOnlyList<string> UnmappedSections          // distinct cardholder RawNames with no label mapping
);

public sealed record StatementWindowDto(DateOnly From, DateOnly To, DateOnly IssueDate, DateOnly CutoffDate);

public sealed record WalletAccountDto(string Id, string Name, string CurrencyCode, string AccountType);

public sealed record WalletCategoryDto(string Id, string Name, string? Color);

public sealed record PdfRowDto(
    int Index,
    DateOnly Date,
    decimal SignedAmount,
    string Currency,
    string Description,
    string? CounterParty,
    string CardholderSectionRawName,
    string CardLast4,
    IReadOnlyList<string> MatchedWalletRecordIds,    // empty ⇒ no duplicate
    bool DefaultSelected,                             // FR-014: !duplicate && !currencyMismatch
    bool CurrencyMismatch                              // always false in this iteration (research §9)
);

public sealed record WalletRowDto(
    string Id,
    DateOnly Date,
    decimal SignedAmount,
    string Currency,
    string? Note,
    string? CounterParty,
    string? CategoryName,
    IReadOnlyList<int> ClaimedByPdfIndices            // every PDF row index that matched this wallet row
);
```

**Determinism**: `PdfRows` is in document order (`Index` ascending). `WalletRows` is `(date ASC, id ASC)`. `MatchedWalletRecordIds` is lexicographic. `ClaimedByPdfIndices` is ascending. `UnmappedSections` is alphabetic.

### `SubmitRequest`

```csharp
public sealed record SubmitRequest(
    string AccountId,
    IReadOnlyList<SubmitRequestRow> Rows
);

public sealed record SubmitRequestRow(
    int Index,
    DateOnly Date,
    decimal SignedAmount,
    string Currency,
    string Description,
    string? CounterParty,
    string CardholderSectionRawName,
    string CategoryId               // required; the endpoint 400s if missing per row
);
```

Validation in the endpoint:
- `AccountId` non-empty.
- `Rows` non-empty.
- Every `SubmitRequestRow.CategoryId` non-empty (else `400 Bad Request` with `WALLET_REJECTED`-shaped envelope and a message naming the offending row indices).

### `SubmitResponse`

```csharp
public sealed record SubmitResponse(IReadOnlyList<SubmitOutcomeDto> Outcomes);

public sealed record SubmitOutcomeDto(
    int Index,                       // mirrors SubmitRequestRow.Index
    bool Ok,
    string? WalletRecordId,
    string? ErrorMessage
);
```

### `WalletErrorResponse`

```csharp
public sealed record WalletErrorResponse(ErrorBody Error);
// Reuses ErrorBody { string Code, string Message } from existing CardStatement.Api.Contracts.
```

`Code` ∈ { `WALLET_NOT_CONFIGURED`, `WALLET_CREDENTIALS_INVALID`, `WALLET_UNAVAILABLE`, `WALLET_REJECTED` }.

**Backs**: FR-008, FR-009, FR-011, FR-012, FR-013, FR-014, FR-015, FR-017, FR-019, FR-020.

---

## 7. Endpoint surface

Mapped in `WalletImportEndpoint.cs`:

| Endpoint | Method | Body | Success | Failures |
|---|---|---|---|---|
| `/api/wallet/accounts` | GET | — | `200 { accounts: WalletAccountDto[] }` (archived filtered out) | 503 NotConfigured, 502 CredentialsInvalid, 504 Unavailable, 502 Rejected |
| `/api/wallet/categories` | GET | — | `200 { categories: WalletCategoryDto[] }` | same as above |
| `/api/wallet/import/compare` | POST | `multipart/form-data`: `file`, `accountId` | `200 CompareResponse` | 400/413/422 existing extraction envelope **plus** the four Wallet codes above for Wallet-side failures |
| `/api/wallet/import/submit` | POST | `application/json`: `SubmitRequest` | `200 SubmitResponse` | 400 input validation, plus the four Wallet codes above |

The PDF guards (size, magic-byte, empty) live in a shared `PdfUploadGuard` helper used by both `ExtractEndpoint` and `WalletImportEndpoint`. Failures there use the existing `ExtractionErrorResponse` envelope (FR-006).

---

## 8. State transitions (frontend page)

The frontend `WalletImportPage` is a `useReducer` state machine:

```text
loadingAccounts → accountsReady → uploading → comparing → comparisonReady ⇄ submitting → outcomeReady
                       ↓                                            ↓                          ↓
                  walletError                                  walletError              walletError
                  (retry)                                      (retry)                  (retry)
```

State payloads:

| State | Payload |
|---|---|
| `loadingAccounts` | — |
| `accountsReady` | `{ accounts, categories }` |
| `uploading` | `{ accounts, categories, accountId }` |
| `comparing` | (same) |
| `comparisonReady` | `{ accounts, categories, accountId, compare: CompareResponse, selections, categoryByIndex }` |
| `submitting` | (`comparisonReady` payload) |
| `outcomeReady` | (`comparisonReady` payload) + `{ outcomes: SubmitOutcomeDto[] }` |
| `walletError` | `{ code: WalletErrorCode, message: string, previousState: <name> }` |

Categories are fetched once with the compare response (the `CompareResponse.categories` array). They are NOT re-fetched mid-flow.

**Backs**: FR-021 (reload action transitions `outcomeReady` → `comparing`), spec edge case "category renamed mid-flow" (compare's categories are a snapshot; submit failures carry the row error verbatim).

---

## 9. Privacy / logging contract

| Concern | Where enforced |
|---|---|
| `WalletOptions.Jwt` never logged | `WalletApiClient` redacts the Authorization header in any logger middleware; the value never appears in any log statement. |
| PDF row descriptions not logged at default level | `WalletImportService` logs counts and outcomes; raw `Description` is never logged at `Information`. `Debug`-level traces with descriptions are allowed but off by default. |
| Wallet record `Note` not logged | Same rule as `Description`. |
| `WalletApiException.BodyExcerpt` logged at `Warning` | Bounded to 256 chars and only when the call already failed; needed for diagnosing operator mistakes (wrong tenant, wrong base URL). |

**Backs**: FR-027.

---

## 10. What this feature does NOT add to the data model

- No new `CardStatement.Core` types. The Statement / Transaction / CardholderSection / Direction / RowType / ReconciliationStatus types from `001`/`002` are reused unchanged.
- No new fields on existing extraction DTOs. `ExtractedStatementResponse`, `TransactionDto`, `CardholderSectionDto`, etc. are unchanged. The Wallet flow projects from `Core` models directly into its own `PdfRowInternal` and DTOs.
- No persistence types — there is no database.
- No identity/user types — the backend is single-tenant.
