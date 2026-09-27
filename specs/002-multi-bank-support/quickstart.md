# Quickstart: Multi-Bank Backend

**Phase**: 1 (Design & Contracts) | **Feature**: `002-multi-bank-support` | **Date**: 2026-05-29

This doc has two audiences:

1. **A developer running the refactored backend** for the first time and verifying the existing BAC behavior still works end-to-end (Section A).
2. **A developer adding a new bank** later (Section B). This section is intentionally a step-by-step recipe — if it ever takes longer than ~15 minutes to follow on a normal laptop, the refactor failed at its only real goal.

The refactor itself is **frontend-invisible**: no instructions for the `frontend/` tree appear here because nothing in it changes.

---

## A. Verify BAC still works after the refactor

### Prereqs (same as `001-pdf-extract-web`)

- .NET 10 SDK (matches `global.json` → `10.0.201`)
- `pnpm` only if you want to also re-run the existing frontend tests; the backend verification below does not require the frontend to be running.
- The sample PDF at `samples/final5140_45178439_316493_0.pdf`.

### A.1. Build & run the API

```bash
dotnet build CreditStatementParser.slnx
dotnet run --project src/CardStatement.Api
```

Expected startup log lines (new in 0.2.0):

```text
info: Program[0] Registered banks: bac (BAC Credomatic (El Salvador))
info: Microsoft.Hosting.Lifetime[14] Now listening on: http://localhost:5080
```

If you see `EmptyBankRegistryException` instead, `Program.cs` is missing the `services.AddBacBank()` line — that is the intended loud failure from FR-015 / SC-007.

### A.2. Extract the sample PDF

```bash
curl -s -X POST http://localhost:5080/api/statements/extract \
  -F "file=@samples/final5140_45178439_316493_0.pdf" \
  -o /tmp/extract-002.json
```

### A.3. Confirm byte-for-byte parity on existing fields (SC-002)

Generate the same response from the **pre-refactor baseline** (the `main` branch tag before this work, or a known-good snapshot you saved before starting). Then diff *with the new `bank` field stripped*:

```bash
# Strip the new `bank` key from the 0.2.0 response so we compare like-for-like
jq 'del(.bank)' /tmp/extract-002.json > /tmp/extract-002-stripped.json

# Compare to the 0.1.0 baseline (capture this BEFORE merging the refactor!)
diff /tmp/extract-001-baseline.json /tmp/extract-002-stripped.json
```

Expected: empty diff. That is SC-002.

### A.4. Confirm the additive `bank` field is present (FR-009)

```bash
jq '.bank' /tmp/extract-002.json
```

Expected:

```json
{
  "id": "bac",
  "displayName": "BAC Credomatic (El Salvador)"
}
```

### A.5. Run the existing test suites (SC-001)

```bash
dotnet test CreditStatementParser.slnx
```

Every existing test from `tests/CardStatement.Tests` and `tests/CardStatement.Api.Tests` must pass without modification. The new test classes (`BankRegistryTests`, `BankResolverTests`, `MultiBankRoutingTests`, `EmptyRegistryStartupTests`) also pass.

If you want the frontend integration test too:

```bash
cd frontend && pnpm test
```

Expected: passes unchanged (FR-009).

---

## B. Add a new bank in ~15 minutes

Worked example: add a fictional **Banco X** bank that recognizes PDFs containing the marker word `BANCOX-DEMO` on page 1 and returns a minimal `Statement`. Use this exact recipe as a template for real banks; only the detector and parser internals change.

> ⚠️ **The only edits to pre-existing files** in this recipe are (1) one `services.AddBancoXBank();` line in `Program.cs` and (2) the equivalent line in `CardStatement.App`'s DI bootstrap if you want the CLI app to recognize the bank too. Every other change is a new file. This is the literal claim of SC-003; if your real bank requires editing anything else, stop and re-read [research.md](./research.md) to see which decision you're working against.

### B.1. Create the bank folder and provider

```bash
mkdir -p src/CardStatement.Core/Banks/BancoX
```

`src/CardStatement.Core/Banks/BancoX/BancoXBankProvider.cs`:

```csharp
using CardStatement.Core.Abstractions;
using CardStatement.Core.Banks;
using CardStatement.Core.Models;

namespace CardStatement.Core.Banks.BancoX;

public sealed class BancoXBankProvider : IBankProvider
{
    private static readonly BankInfo TheBank = new("banco-x", "Banco X (Sample)");

    public BankInfo Info => TheBank;

    public BankDetection Detect(PdfDocumentWords words)
    {
        var hasMarker = words.Words.Any(w =>
            w.PageNumber == 1 &&
            string.Equals(w.Text, "BANCOX-DEMO", StringComparison.Ordinal));

        return hasMarker
            ? BankDetection.Match(BankDetection.HighConfidence, "marker word on page 1")
            : BankDetection.NoMatch();
    }

    public Statement Parse(PdfDocumentWords words)
    {
        // Real banks delegate to their own parser, table locator, etc. — all
        // private to this folder. Here we just emit a minimal valid Statement.
        return new Statement
        {
            CardType = "BANCOX DEMO",
            MaskedAccount = "0000-0000-0000-0000",
            Period = new StatementPeriod(
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 31)),
            PageCount = words.PageCount,
            Sections = [],
            PrintedTotalCharges = 0m,
            PrintedTotalCredits = 0m,
        };
    }
}
```

### B.2. Add the DI extension method

`src/CardStatement.Core/Banks/BancoX/BancoXServiceCollectionExtensions.cs`:

```csharp
using CardStatement.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CardStatement.Core.Banks.BancoX;

public static class BancoXServiceCollectionExtensions
{
    public static IServiceCollection AddBancoXBank(this IServiceCollection services)
    {
        services.AddSingleton<IBankProvider, BancoXBankProvider>();
        return services;
    }
}
```

### B.3. Register the bank in `Program.cs`

In `src/CardStatement.Api/Program.cs`, add one line below `AddBacBank()`:

```csharp
builder.Services.AddCardStatementCore();
builder.Services.AddBacBank();
builder.Services.AddBancoXBank();   // <— the only edit to a pre-existing file
```

(Do the same in `src/CardStatement.App/Program.cs` if you want the CLI app to recognize Banco X PDFs.)

### B.4. Add the bank's tests

`tests/CardStatement.Tests/Banks/BancoX/BancoXBankProviderTests.cs`:

```csharp
using CardStatement.Core.Banks.BancoX;
using CardStatement.Core.Models;
using Xunit;

namespace CardStatement.Tests.Banks.BancoX;

public class BancoXBankProviderTests
{
    private static PdfDocumentWords WordsWith(params string[] page1Texts)
    {
        var words = page1Texts
            .Select((t, i) => new PdfWord(1, t, X: i * 10, Y: 100, Width: 5, Height: 5))
            .ToList();
        return new PdfDocumentWords(PageCount: 1, Words: words);
    }

    [Fact]
    public void Detect_returns_Match_with_HighConfidence_when_marker_present()
    {
        var sut = new BancoXBankProvider();
        var detection = sut.Detect(WordsWith("HELLO", "BANCOX-DEMO", "WORLD"));

        Assert.True(detection.Matched);
        Assert.Equal(BankDetection.HighConfidence, detection.Confidence);
    }

    [Fact]
    public void Detect_returns_NoMatch_when_marker_absent()
    {
        var sut = new BancoXBankProvider();
        var detection = sut.Detect(WordsWith("HELLO", "WORLD"));

        Assert.False(detection.Matched);
        Assert.Equal(0, detection.Confidence);
    }

    [Fact]
    public void Parse_returns_Statement_with_expected_identity()
    {
        var sut = new BancoXBankProvider();
        var statement = sut.Parse(WordsWith("BANCOX-DEMO"));

        Assert.Equal("BANCOX DEMO", statement.CardType);
        Assert.Equal(1, statement.PageCount);
    }
}
```

### B.5. Verify everything still works

```bash
dotnet build CreditStatementParser.slnx
dotnet test  CreditStatementParser.slnx
dotnet run   --project src/CardStatement.Api
```

The startup log now reads:

```text
info: Program[0] Registered banks: bac (BAC Credomatic (El Salvador)), banco-x (Banco X (Sample))
```

### B.6. Exercise the new bank end-to-end

Generate a tiny one-page PDF whose text contains `BANCOX-DEMO` (any tool will do; for a quick hack a `text-to-pdf` shell utility or a Word "Save as PDF" works). POST it:

```bash
curl -s -X POST http://localhost:5080/api/statements/extract \
  -F "file=@/tmp/bancox-demo.pdf" \
  | jq '.bank'
```

Expected:

```json
{
  "id": "banco-x",
  "displayName": "Banco X (Sample)"
}
```

POST the BAC sample PDF again — it should still route to BAC (SC-006, regression gate from US1 still holds):

```bash
curl -s -X POST http://localhost:5080/api/statements/extract \
  -F "file=@samples/final5140_45178439_316493_0.pdf" \
  | jq '.bank.id'
# → "bac"
```

POST a PDF neither bank recognizes (a blank page works):

```bash
curl -s -X POST http://localhost:5080/api/statements/extract \
  -F "file=@/tmp/blank.pdf" \
  | jq '.error.code'
# → "UNRECOGNIZED_LAYOUT"
```

All three are SC-004.

---

## C. Common pitfalls (and how the design catches them)

| If you... | The design fails you loudly at... | Why |
|---|---|---|
| Forget to call `services.AddBancoXBank()` in `Program.cs` | First request (or startup, due to the eager `GetRequiredService<IBankRegistry>()` call) | The bank isn't in the registry, so its detector never runs. Your test posting a `BANCOX-DEMO` PDF gets `UNRECOGNIZED_LAYOUT`. |
| Reuse an existing `BankInfo.Id` (e.g. accidentally `"bac"`) | App startup | `BankRegistry` constructor throws `DuplicateBankIdException`. |
| Mutate state across `Detect` / `Parse` calls | Concurrency tests (intermittent failures) | Bank providers are singletons (FR-013); any per-request state belongs in locals, not fields. |
| Set `Statement.ReconciliationStatus` inside your `Parse` | `Reconciler` overwrites it (silently — but the reconciliation tests will catch any mismatch) | That field is owned by `Reconciler`; banks only fill rows and printed totals. |
| Make detection slow (e.g. a regex over every word on every page) | The SC-005 latency test (≤ +10%) | Detection is per-request, per-bank; keep it cheap. Look at small, decisive signals (BIN, header text) first. |
| Throw inside `Detect` | An error-level log named after your bank; otherwise nothing user-visible | The resolver catches and treats as `NoMatch` (FR-008). The log line is your nudge to fix it. |
| Throw inside `Parse` after a positive `Detect` | The client gets `UNRECOGNIZED_LAYOUT`; a warning-level log names your bank | The resolver wraps as `UnrecognizedLayoutException` (FR-011). Treat it as a test failure. |

---

## D. What this quickstart deliberately does NOT cover

- **Layout-version routing within one bank.** If Banco X changes its statement format in 2027, your `BancoXBankProvider` itself routes between layouts internally. Adding a separate `IBankProvider` for "Banco X 2027" would re-trigger the ambiguity warning unnecessarily.
- **Per-bank custom error codes.** All bank failures map to the existing `UNRECOGNIZED_LAYOUT` (FR-010). If a bank wants nuanced errors, it logs them.
- **Asynchronous detection or parse.** The seam is intentionally synchronous (→ research D1 implications). If you need to fan out to an external service, do it inside `Parse` synchronously for now and revisit the seam shape when the need is concrete.
- **Hot reload of banks.** Adding a bank requires a rebuild and restart. This is intentional (spec Out-of-scope).
