# Data Model: Multi-Bank Backend Support

**Phase**: 1 (Design & Contracts) | **Feature**: `002-multi-bank-support` | **Date**: 2026-05-29

This document is the implementation-level companion to `spec.md`'s Key Entities section. It specifies the concrete C# shapes for the new bank seam, the additive response field, and the invariants each type carries. Every shape below traces back to a decision in [research.md](./research.md) (cited inline as `→ DN`).

Conventions:
- All new types live in `src/CardStatement.Core/` namespaces (`CardStatement.Core.Abstractions`, `CardStatement.Core.Banks`, `CardStatement.Core.Banks.Bac`, etc.).
- All new types are `public sealed` records unless noted otherwise. Records give us value-based equality (useful in tests) and concise declarations.
- Strings on identity fields are non-empty, no leading/trailing whitespace; enforced via constructor guards on the record using `ArgumentException.ThrowIfNullOrWhiteSpace`.
- No type uses inheritance; the `IBankProvider` seam is interface-based composition only.

---

## 1. `BankInfo` — bank identity

**Namespace**: `CardStatement.Core.Banks`

```csharp
public sealed record BankInfo(string Id, string DisplayName);
```

| Field | Type | Semantics |
|---|---|---|
| `Id` | `string` | Stable machine-readable identifier (e.g. `"bac"`, `"banco-x"`). Lowercase, ASCII letters/digits/hyphens, max ~32 chars. **Constant for the lifetime of a bank** — used as a stable tie-breaker (→ D4) and as the value emitted in the response's `bank.id` field. Renaming a bank's id is a breaking change. |
| `DisplayName` | `string` | Human-readable, may include spaces, accents, country qualifier (e.g. `"BAC Credomatic (El Salvador)"`). Surfaced in the response's `bank.displayName`, in startup logs (FR-016), and in ambiguity warnings (FR-018). |

**Invariants** (enforced in constructor):
- Both fields are non-null, non-whitespace.
- `Id` matches `^[a-z0-9][a-z0-9-]{0,31}$`. Invalid id ⇒ `ArgumentException` at construction (so a misconfigured bank fails fast at startup, not at first request).

**Backs**: FR-001 (identity), FR-009 (additive bank field), FR-016 (startup log), FR-018 (ambiguity log).

---

## 2. `BankDetection` — per-bank detection outcome

**Namespace**: `CardStatement.Core.Banks`

```csharp
public sealed record BankDetection(bool Matched, int Confidence, string? Reason)
{
    public const int HighConfidence = 90;
    public const int MediumConfidence = 50;
    public const int LowConfidence = 10;

    public static BankDetection NoMatch(string? reason = null) =>
        new(Matched: false, Confidence: 0, Reason: reason);

    public static BankDetection Match(int confidence, string? reason = null) =>
        new(Matched: true, Confidence: confidence, Reason: reason);
}
```

| Field | Type | Semantics |
|---|---|---|
| `Matched` | `bool` | `true` if this bank claims the PDF. Drives whether the resolver considers this entry a candidate. |
| `Confidence` | `int` | When `Matched = true`, an integer in `[1, 100]`. Used by the resolver as the primary tie-breaker (→ D4). When `Matched = false`, MUST be `0`. |
| `Reason` | `string?` | Optional short, log-friendly description (e.g. `"BIN 459378 found on page 1"`). For operator visibility only; never exposed to the client. Stripped of PII by the bank author (the bank's own concern). |

**Invariants** (enforced in constructor):
- `Matched = true` ⇒ `Confidence ∈ [1, 100]`. Otherwise ⇒ `ArgumentOutOfRangeException`.
- `Matched = false` ⇒ `Confidence == 0`. Otherwise ⇒ `ArgumentOutOfRangeException`.
- `Reason` is allowed to be null or empty; it is never required.

**Backs**: FR-004 (detection result drives selection), FR-006 (confidence is the tie-breaker), FR-018 (reason feeds the log).

---

## 3. `IBankProvider` — the seam

**Namespace**: `CardStatement.Core.Abstractions`

```csharp
public interface IBankProvider
{
    BankInfo Info { get; }
    BankDetection Detect(PdfDocumentWords words);
    Statement Parse(PdfDocumentWords words);
}
```

| Member | Semantics |
|---|---|
| `Info` | The bank's stable identity. Returning a different `BankInfo` over the lifetime of the provider is undefined behavior (the resolver and registry assume it is stable). Implementations typically return a `static readonly BankInfo` instance. |
| `Detect(words)` | Pure function: given the words extracted from one PDF, decide whether this bank claims the PDF and with what confidence. MUST be safe to call concurrently. MUST be deterministic — same input ⇒ same output. SHOULD be fast (microseconds–single-digit milliseconds for typical PDFs). MAY throw — the resolver contains the throw (→ D5) — but bank authors are expected to avoid this. |
| `Parse(words)` | Given the words, produce a fully-populated `Statement` (header, sections, transactions, printed totals — but NOT `ReconciliationStatus`, which the shared `Reconciler` fills in afterward). Called only when `Detect` matched. Throwing here surfaces as `UNRECOGNIZED_LAYOUT` to the client (→ D6). Same concurrency and determinism contracts as `Detect`. |

**Constraints on implementations** (verified by tests, not the compiler):
- An `IBankProvider` MUST NOT hold mutable state across `Detect`/`Parse` calls. The provider singleton is shared across requests (FR-013).
- An `IBankProvider` MUST NOT touch the shared registry, the response DTOs, the endpoint, or any other bank (FR-014).
- An `IBankProvider`'s `Parse` MUST NOT set `Statement.ReconciliationStatus` — that field is owned by the `Reconciler`.

**Backs**: FR-001, FR-002, FR-007 (input is already-extracted words), FR-013, FR-014.

---

## 4. `IBankRegistry` — read-only view of registered banks

**Namespace**: `CardStatement.Core.Abstractions`

```csharp
public interface IBankRegistry
{
    IReadOnlyList<IBankProvider> Providers { get; }
}
```

Concrete: `CardStatement.Core.Banks.BankRegistry : IBankRegistry`

```csharp
public sealed class BankRegistry : IBankRegistry
{
    public IReadOnlyList<IBankProvider> Providers { get; }

    public BankRegistry(IEnumerable<IBankProvider> providers)
    {
        var snapshot = providers.ToImmutableArray();
        if (snapshot.IsEmpty)
            throw new EmptyBankRegistryException();

        // Detect duplicate ids early — they would silently change the tie-break in D4.
        var duplicates = snapshot
            .GroupBy(p => p.Info.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();
        if (duplicates.Length > 0)
            throw new DuplicateBankIdException(duplicates);

        Providers = snapshot;
    }
}
```

**Invariants**:
- `Providers` is non-empty (`EmptyBankRegistryException` otherwise — FR-015 / SC-007).
- Bank ids are unique within the registry (`DuplicateBankIdException` otherwise — protects FR-006's deterministic tie-break).
- `Providers` is an immutable snapshot taken at construction; mutating the source `IEnumerable` afterward has no effect on the registry. Required for FR-013 (thread-safety) and FR-012 (determinism).

**Lifetime**: registered as `Singleton` by `AddCardStatementCore()` (→ D7). DI's eager resolution from `Program.cs` (→ D8) ensures the constructor's invariants fail loudly at startup.

**Backs**: FR-015, SC-007, FR-013.

---

## 5. `BankResolutionResult` — internal carrier

**Namespace**: `CardStatement.Core.Banks`

```csharp
internal sealed record BankResolutionResult(IBankProvider Provider, Statement Statement);
```

Internal because callers (the endpoint) only need `{ BankInfo, Statement }` — the provider instance leaks an implementation detail. The API layer projects this into `BankInfo` + `Statement` before mapping to DTOs.

---

## 6. `IBankResolver` / `BankResolver` — orchestrator

**Namespace**: `CardStatement.Core.Abstractions` (interface), `CardStatement.Core.Banks` (impl)

```csharp
public interface IBankResolver
{
    // Returns the chosen bank + the parsed statement. Throws NoBankMatchedException
    // if no bank claimed the input.
    (BankInfo Bank, Statement Statement) Resolve(PdfDocumentWords words);
}
```

**Algorithm** (implementation in `BankResolver`):

1. Compute `candidates = []`.
2. For each `provider` in `registry.Providers` (in registry order, which is registration order):
   - Try `var detection = provider.Detect(words);`
     - On exception: log error with `{ provider.Info.Id, ex.GetType().Name, ex.Message }`, treat as `BankDetection.NoMatch()` (→ D5 / FR-008).
   - If `detection.Matched`, append `(provider, detection)` to `candidates`.
3. If `candidates.Length == 0`:
   - Log info `"No bank matched; returning UNRECOGNIZED_LAYOUT"` (FR-018).
   - Throw `NoBankMatchedException`. (Endpoint layer maps this to `UNRECOGNIZED_LAYOUT`; → D6 / FR-005.)
4. If `candidates.Length > 1`:
   - Log warning `"Ambiguous detection: <id1>(conf=X, reason='...'), <id2>(conf=Y, reason='...')"` (FR-006, FR-018).
   - Sort by `(-Confidence, Provider.Info.Id ordinal)`. Take the first. (→ D4)
5. Otherwise `candidates.Length == 1` → that's the winner. Log info `"Bank selected: {id}"` (FR-017, FR-018).
6. Try `var statement = winner.Provider.Parse(words);`
   - On exception: log warning `{ winner.Info.Id, ex.GetType().Name, ex.Message }`. Throw `UnrecognizedLayoutException("Bank '{id}' could not parse the PDF.", ex)` (→ D6 / FR-011).
7. Return `(winner.Info, statement)`.

**Concurrency**: stateless after construction; safe to call concurrently (FR-013).
**Determinism**: given the same `PdfDocumentWords` and the same registry, returns the same `(Bank, Statement)` every time across process restarts (FR-012, SC-008).
**Lifetime**: registered as `Singleton` by `AddCardStatementCore()`.

**Backs**: FR-004, FR-005, FR-006, FR-008, FR-011, FR-012, FR-013, FR-017, FR-018, SC-006, SC-008.

---

## 7. Exceptions

**Namespace**: `CardStatement.Core.Banks.Exceptions`

```csharp
public sealed class NoBankMatchedException : Exception
{
    public NoBankMatchedException() : base("No registered bank recognized the uploaded PDF.") { }
}

public sealed class EmptyBankRegistryException : Exception
{
    public EmptyBankRegistryException()
        : base("BankRegistry was constructed with zero IBankProvider implementations. " +
               "Did you forget to call services.AddBacBank() (or another bank's registration extension) in Program.cs?") { }
}

public sealed class DuplicateBankIdException : Exception
{
    public IReadOnlyList<string> DuplicateIds { get; }

    public DuplicateBankIdException(IReadOnlyList<string> duplicateIds)
        : base($"Multiple IBankProvider implementations registered with the same Id(s): {string.Join(", ", duplicateIds)}.")
    {
        DuplicateIds = duplicateIds;
    }
}
```

`UnrecognizedLayoutException` (already in `CardStatement.Api.ErrorHandling`) is reused — no new exception type is needed for the parser-throws-after-match case (→ D6).

**Mapping in the API layer** (`ExtractionFailureMapper`):
- `NoBankMatchedException` → 422 `UNRECOGNIZED_LAYOUT` (existing message generalized: drop the BAC-specific wording).
- `UnrecognizedLayoutException` → 422 `UNRECOGNIZED_LAYOUT` (unchanged).
- All other existing exception → existing mappings (unchanged, FR-010).

---

## 8. `BankInfoDto` — the response field

**Namespace**: `CardStatement.Api.Contracts`

```csharp
public sealed record BankInfoDto(string Id, string DisplayName);
```

Simple JSON projection of `BankInfo`. Serialized as `{ "id": "bac", "displayName": "BAC Credomatic (El Salvador)" }`.

`ExtractedStatementResponse` is updated to:

```csharp
public sealed record ExtractedStatementResponse(
    StatementHeaderDto Statement,
    IReadOnlyList<CardholderSectionDto> Sections,
    StatementTotalsDto Totals,
    ReconciliationStatus ReconciliationStatus,
    int NeedsReviewCount,
    IReadOnlyList<string> UnmappedCards,
    BankInfoDto Bank                        // <— NEW, appended LAST
);
```

**Why last**: System.Text.Json emits properties in declaration order; appending preserves the byte sequence of every existing JSON key, satisfying SC-002. (→ D10.)

`StatementMapper.ToResponse` gains a `BankInfo` parameter:

```csharp
public static ExtractedStatementResponse ToResponse(Statement statement, BankInfo bank)
```

The endpoint passes the `BankInfo` it receives from `IBankResolver.Resolve`.

**Backs**: FR-009, SC-002.

---

## 9. `IServiceCollection` extensions

**Namespace**: `CardStatement.Core.Registration` (and `CardStatement.Core.Banks.Bac` for the BAC extension)

```csharp
// CoreServiceCollectionExtensions.cs
public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddCardStatementCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IPdfExtractor, PdfPigExtractor>();
        services.TryAddSingleton<IReconciler, Reconciler>();
        services.TryAddSingleton<IBankRegistry, BankRegistry>();
        services.TryAddSingleton<IBankResolver, BankResolver>();
        return services;
    }
}

// BacServiceCollectionExtensions.cs (under Banks/Bac/)
public static class BacServiceCollectionExtensions
{
    public static IServiceCollection AddBacBank(this IServiceCollection services)
    {
        services.AddSingleton<IBankProvider, BacBankProvider>();
        return services;
    }
}
```

**Why `TryAddSingleton` for core, `AddSingleton` for the bank**: core can be added by both `CardStatement.Api` and `CardStatement.App` (idempotent); banks are always added explicitly once per process (deliberately non-idempotent so double-registration shows up as a `DuplicateBankIdException` from `BankRegistry`'s constructor — the friendliest possible failure for a copy-paste mistake).

**Backs**: FR-002 (additive registration), FR-007 (core composed once), FR-013 (singletons).

---

## 10. The startup eager-resolution call (in `Program.cs`)

```csharp
var app = builder.Build();

// → D8: Force BankRegistry construction so an empty/invalid registry fails
//      before the server starts accepting requests. Also emits the FR-016
//      "registered banks" log line.
var registry = app.Services.GetRequiredService<IBankRegistry>();
app.Logger.LogInformation(
    "Registered banks: {Banks}",
    string.Join(", ", registry.Providers.Select(p => $"{p.Info.Id} ({p.Info.DisplayName})")));
```

**Backs**: FR-015, FR-016, SC-007.

---

## 11. State-transition / lifecycle notes

The shared `Statement` model is unchanged. The only state transition this spec introduces is **resolution**:

```
PdfDocumentWords
   │
   ▼  (IBankResolver.Resolve — D5/D6 contain failures)
(BankInfo, Statement)        ← per-section sums are populated by the bank parser
   │
   ▼  (Reconciler.Reconcile — unchanged from 001)
(BankInfo, Statement with ReconciliationStatus filled)
   │
   ▼  (StatementMapper.ToResponse(statement, bank))
ExtractedStatementResponse   ← `bank` field is set here, last property
```

No state survives request boundaries.

---

## 12. Trace matrix (entity → spec items)

| Entity / type | Backs |
|---|---|
| `BankInfo` | FR-001, FR-009, FR-016, FR-018 |
| `BankDetection` | FR-004, FR-006, FR-018 |
| `IBankProvider` | FR-001, FR-002, FR-007, FR-013, FR-014 |
| `IBankRegistry` / `BankRegistry` | FR-013, FR-015, SC-007 |
| `IBankResolver` / `BankResolver` | FR-004, FR-005, FR-006, FR-008, FR-011, FR-012, FR-013, FR-017, FR-018, SC-006, SC-008 |
| `NoBankMatchedException` | FR-005 |
| `EmptyBankRegistryException` | FR-015, SC-007 |
| `DuplicateBankIdException` | FR-006 (protects tie-break) |
| `BankInfoDto` + `ExtractedStatementResponse` append | FR-009, SC-002 |
| `AddCardStatementCore` / `AddBacBank` | FR-002, FR-003, FR-007, FR-013 |
| Startup eager-resolve | FR-015, FR-016, SC-007 |
