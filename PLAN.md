# Implementation Plan — Credit Card Statement Parser PoC

Companion to `SPEC.md`. The spec defines *what* and *why*; this plan defines
*how* and *in what order* to build it. Where the spec is authoritative
(§3 extraction/categorization split, §5 BAC grammar, §5.6 direction rule,
§8 output contract, §13 gotchas), the plan does not restate it — it links back.

## 0. Decisions locked in before coding

| Decision | Choice | Notes |
|---|---|---|
| Target framework | **`net10.0`** | SDK `10.0.201` confirmed installed. |
| Solution layout | **App + Core + Tests** (3 projects) | Slim split; can be sharded later per spec §6. |
| LLM provider | **OpenAI** | Hidden behind `ILlmClient`; stubbed by default, live behind config flag. |
| Live APIs | **Real Categories + Labels calls during dev** | Token via `dotnet user-secrets`; committed `appsettings.json` only has placeholders. |
| Test framework | **xUnit** | Plus `FluentAssertions` for readability. |
| Output | JSON primary, optional CSV, console summary (spec §8). | |

## 1. Solution layout

```
CreditStatementParser.sln
├── src/
│   ├── CardStatement.Core/           class library (net10.0)
│   │   ├── Models/                   Statement, CardholderSection, Transaction, EnrichedRecord, Category, Label, enums
│   │   ├── Abstractions/             ILlmClient, ICategoryApi, ILabelsApi, IPdfExtractor, IStatementParser, IReconciler
│   │   ├── Pdf/                      PdfPig extractor, table locator, row builder
│   │   ├── Parsing/                  BAC grammar: section detector, row classifier, date resolver, direction tagger
│   │   ├── Reconciliation/           subtotal/total reconciler → ReconciliationStatus
│   │   ├── Labels/                   card-last4 → label id resolver, validator
│   │   ├── Categorization/           OpenAI client, taxonomy filter, fixed-category resolver, batch orchestrator
│   │   ├── Apis/                     HTTP clients for Categories (paginated) + Labels
│   │   └── Result/                   ResultBuilder → enriched records + totals
│   └── CardStatement.App/            console app (net10.0)
│       ├── Program.cs                composition root (Microsoft.Extensions.Hosting + DI + Configuration)
│       ├── appsettings.json          committed; placeholders only
│       ├── appsettings.Development.json  git-ignored; never committed
│       └── Output/                   JsonWriter, CsvWriter, ConsoleSummaryPrinter
└── tests/
    └── CardStatement.Tests/          xUnit (net10.0)
        ├── Fixtures/                 sample PDF copy, embedded category/label JSON from spec §7
        ├── Pdf/                      coordinate extraction smoke test
        ├── Parsing/                  unit tests per spec §11
        ├── Reconciliation/
        ├── Categorization/           uses fake ILlmClient
        ├── Apis/                     pagination follows nextOffset; archived labels filtered
        └── EndToEnd/                 golden-file test (PDF → JSON)
```

`Directory.Build.props` at repo root: `<TargetFramework>net10.0</TargetFramework>`,
nullable enabled, treat warnings as errors, implicit usings on.

## 2. NuGet dependencies

| Package | Where | Purpose |
|---|---|---|
| `UglyToad.PdfPig` | Core | PDF words + bounding boxes (spec §3, §4). |
| `Microsoft.Extensions.Hosting` | App | Generic host: config, DI, logging. |
| `Microsoft.Extensions.Configuration.UserSecrets` | App | Bearer token + LLM key out of source. |
| `Microsoft.Extensions.Http` | Core | Typed `HttpClient` for Categories/Labels/OpenAI. |
| `System.CommandLine` (beta) *or* manual `args` parse | App | `<pdf> [--out file.json] [--csv file.csv]`. Manual is fine for PoC. |
| `OpenAI` (official .NET SDK) | Core | LLM client implementation. Wrapped behind `ILlmClient`. |
| `xunit`, `xunit.runner.visualstudio` | Tests | |
| `FluentAssertions` | Tests | |
| `Microsoft.NET.Test.Sdk` | Tests | |

## 3. Build order (gated milestones)

Each milestone ends with a runnable demo or green test before moving on.

### M1 — Skeleton + sample wired up
- `dotnet new sln`, three projects, `Directory.Build.props`, `.gitignore` (must include `appsettings.Development.json`, `.user`, `bin/`, `obj/`).
- Copy `samples/final5140_45178439_316493_0.pdf` reference path into test fixture.
- Stub `Program.cs` that prints args and exits 0.
- **Gate:** `dotnet build` clean, `dotnet run --project src/CardStatement.App -- samples/final5140_45178439_316493_0.pdf` prints the path.

### M2 — Core domain models + abstractions
- Implement records in `Core/Models/` per spec §6.3 and §8.
- Interfaces in `Core/Abstractions/`: `IPdfExtractor`, `IStatementParser`, `IReconciler`, `ICategoryApi`, `ILabelsApi`, `ICategorizer`, `ILlmClient`, `ILabelResolver`, `IResultBuilder`.
- Enums: `Direction { Income, Expense }`, `RowType { Purchase, Financing, Payment, Adjustment }`, `ReconciliationStatus { Ok, Mismatch, NotChecked }`.
- **Gate:** compiles; no behavior yet.

### M3 — PDF extraction (PdfPig) + table isolation
- `PdfWordExtractor`: open PDF, yield per-page list of `(text, x, y, width, height, page)`.
- `TransactionTableLocator`: detect column header band (`FECHA / NUMERO DE REFERENCIA / CONCEPTO / CARGOS / ABONOS`) per page; capture X bands for each column and a Y range (below header, above bottom-slip cutoff). Tolerances in config.
- `RowBuilder`: cluster words by shared Y (configurable tolerance), order by X within row.
- **Gate:** debug-dump tool prints reconstructed rows for sample PDF; manual eyeball vs PDF shows the central table — no payment-slip noise (spec §4).

### M4 — BAC grammar parser + Direction + Reconciler
- Section detector: regex `^459378XXXXXX(\d{4})\s+»»»\s+(.+)$` → opens `CardholderSection`.
- Row classifier: skip filter list (spec §5.4); else parse `MMM/DD  DD/MM  REF  SEQ  desc...  amount`.
- Date resolver: derive year from `FECHA DE EMISION` / `FECHA DE CORTE`; handle Dec→Jan rollover (spec §5.3, §13).
- Direction tagger: by amount-token X coordinate → CARGOS band ⇒ Expense, ABONOS band ⇒ Income (spec §5.6 — single source of truth).
- Subtotal / total capture lines (`SUBTOTAL.:`, `TOTAL ...:`).
- `Reconciler`: per-section and grand-total comparison → `ReconciliationStatus`, mark `NeedsReview` on mismatch (spec §5.7).
- **Gate:** unit tests from spec §11 green: row classification (C/X/P), date rollover, `MASFERRESAN S` description collision, column-driven Income/Expense, reconciliation pass/fail.

### M5 — API clients (Categories + Labels)
- `CategoryApiClient`: paginated GET, follow `agentHints.action.url` *and* `nextOffset` until exhausted; assemble full list; cache for run; **skip empty-name entries**; key by guid (spec §7.1, §13).
- `LabelApiClient`: paginated GET via `limit`/`offset`; filter `archived: true` (spec §7.2).
- Both use a single typed `HttpClient` configured with `BaseUrl` + `Authorization: Bearer <token>` from config.
- Token retrieval order: `appsettings.Development.json` → user-secrets → env var `WALLETSEED_BEARER_TOKEN`. Placeholder in committed `appsettings.json`.
- **Gate:** integration test (skipped by default; runnable locally) hits live API and prints counts. Unit tests use embedded §7 JSON as fakes.

### M6 — Card → Label mapping
- `CardholderLabels` map in `appsettings.json` (spec §7.4 sample values).
- `LabelResolver`: at startup, validate every configured guid exists & is unarchived; warn otherwise.
- Apply at runtime: section `CardLast4` → `LabelId` (+ `LabelName` from API). Missing → `null` + `LabelUnmapped` flag; surface card last-4 + raw name in run summary (spec §7.4, §13).
- **Gate:** unit test for mapped + unmapped cases.

### M7 — Categorizer (LLM + fixed)
- `FixedCategoryResolver`: on startup, resolve names from §6.4 (`Debt`, `Loan, interests`, `Refunds (tax, purchase)`, `Automatic bank statements reading`) to guids from the cached taxonomy; configurable name overrides.
- `LlmCategorizer`:
  - Filters: `Direction == Expense && RowType == Purchase` only (spec §9).
  - Batches 20–50 rows; aligns results by index (echo reference number to detect drift).
  - Prompt includes allowed `[id, name]` pairs (empty names removed); low temperature; instructs single id per row.
  - **Validates** each returned id is in allowed set; invalid → null + `NeedsReview` *or* fallback bucket `40b565bb-...` (configurable; default = fallback, spec §6.4).
- `ILlmClient` implementations:
  - `StubLlmClient` (default): returns fallback bucket for everything; lets the pipeline run offline / in tests.
  - `OpenAiLlmClient`: official `OpenAI` SDK; model + key from config; selected when `Categorization:Provider=openai`.
- Payments (`P####`) and financing (`X####`) bypass the LLM — fixed categories per direction (spec §6.4).
- **Gate:** unit test with fake `ILlmClient` returning a fixed id; second test rejects invented id.

### M8 — Result builder + outputs
- `ResultBuilder`: merge parsed sections + label + category → `EnrichedRecord` list (spec §8 fields exactly).
- Compute `totalIncome`, `totalExpense` from `direction` (spec §8); assert match against printed TOTAL when `reconciliationStatus == Ok`.
- Writers:
  - `JsonWriter`: System.Text.Json, indented, camelCase, ISO dates.
  - `CsvWriter`: header + rows; emit only when `--csv` provided.
  - `ConsoleSummaryPrinter`: pages, sections (card last-4 + label name + tx count + direction subtotals), totals, reconciliation status, `LabelUnmapped` cards, `NeedsReview` count.
- Exit codes: spec §6.1 — non-zero only on hard parse failure.
- **Gate:** golden-file test runs end-to-end on the sample PDF and produces expected JSON.

### M9 — Polish
- Logging via `Microsoft.Extensions.Logging`; `--verbose` flips to Debug.
- README snippet (only if user asks) covering: run command, secrets setup, sample output.
- Run `dotnet format`; ensure no warnings (warnings-as-errors).

## 4. Configuration (committed `appsettings.json`)

```json
{
  "Api": {
    "BaseUrl": "https://REPLACE_ME/v1/api",
    "BearerToken": "REPLACE_VIA_SECRETS"
  },
  "Categorization": {
    "Provider": "stub",
    "OpenAi": {
      "Model": "gpt-4.1-mini",
      "ApiKey": "REPLACE_VIA_SECRETS"
    },
    "BatchSize": 30,
    "FallbackCategoryId": "40b565bb-d9cc-430a-a4ef-0c8649b636ab",
    "FixedCategoryNames": {
      "Payment": "Debt",
      "FinancingCharge": "Loan, interests",
      "FinancingReversal": "Refunds (tax, purchase)"
    }
  },
  "CardholderLabels": {
    "2533": "c049554c-b118-4e47-9aa5-9f863507cfeb",
    "2640": "7c4fe378-882a-49b2-b7de-3fb076694a01",
    "2706": "936a90c7-01c4-4bf4-805a-59733a925547",
    "4941": "16aa3eb4-e545-47d2-a45a-135b3475ac81",
    "5468": "936a90c7-01c4-4bf4-805a-59733a925547"
  },
  "Parsing": {
    "RowYTolerance": 2.0,
    "ColumnXTolerance": 5.0
  }
}
```

Secrets (`dotnet user-secrets`):
- `Api:BearerToken`
- `Categorization:OpenAi:ApiKey`

`.gitignore` additions: `appsettings.Development.json`, `*.user`, `bin/`, `obj/`,
`.DS_Store`.

## 5. Test strategy

Mirrors spec §11. Concrete test names:

- `Pdf/`: `Extractor_emits_words_with_coordinates_for_all_pages`.
- `Parsing/`:
  - `RowClassifier_identifies_purchase_financing_payment`.
  - `DateResolver_handles_dec_to_jan_rollover`.
  - `DescriptionParser_keeps_collided_branch_text_as_is`.
  - `Direction_is_set_by_amount_column_not_merchant`.
  - `Filter_skips_subtotal_total_puntos_bonificacion`.
- `Reconciliation/`:
  - `Sums_match_printed_subtotal_marks_ok`.
  - `Sum_mismatch_marks_needs_review`.
- `Apis/`:
  - `CategoryClient_follows_nextOffset_pagination`.
  - `CategoryClient_skips_empty_name_entries`.
  - `LabelClient_filters_archived`.
- `Categorization/`:
  - `Categorizer_ignores_payments_and_financing`.
  - `Categorizer_rejects_invented_category_id`.
  - `FixedCategoryResolver_resolves_by_name_from_taxonomy`.
- `Labels/`:
  - `Unmapped_card_returns_null_label_and_flag`.
- `EndToEnd/`:
  - `Sample_pdf_produces_expected_enriched_json` (golden file under `tests/Fixtures/expected.json`).

Fakes are wired manually (no Moq) — the surfaces are small and explicit fakes
read better than mocks for this PoC.

## 6. Open items to revisit during build

- **Golden-file generation** — produced once on first successful run of M4–M8 and committed; later changes require explicit re-baseline.
- **OpenAI model choice** — `gpt-4.1-mini` is the placeholder; revisit after first batch run depending on accuracy/cost.
- **Column band detection** — if header text is not consistently positioned across pages, fall back to a fixed X-band configured in `appsettings.json`.
- **Aggregator prefixes** (spec §5.5: `WOMPI*`, `N1CO*`, `PAGADITO*`) — pass through raw to the LLM as spec instructs; revisit only if accuracy is poor.
- **CSV format** — basic header + rows now; quoting/escaping per RFC 4180 only as needed.

## 7. Out of scope (per spec §2)

Write-back to external systems, web API/UI, OCR / scanned PDFs, other banks,
database persistence, automatic name-based card→label inference. The slim
3-project layout deliberately leaves room to extract `CardStatement.Pdf`,
`.Parsing`, `.Categorization`, `.Labels` later without rewriting consumers.
