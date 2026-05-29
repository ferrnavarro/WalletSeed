# Data Model: PDF Extract & Display (Web MVP)

**Feature**: `001-pdf-extract-web`
**Date**: 2026-05-28
**Source-of-truth contract**: [contracts/openapi.yaml](./contracts/openapi.yaml)

This document describes the **wire shape** the API returns and how each field maps from the existing `CardStatement.Core.Models` types. It is derived from the spec's **Key Entities** section and from the existing console PoC's `result.json` output (already in the repo root) for parity reference.

All field names are **camelCase**. All money amounts are JSON `number` (not strings) and are **always positive** — direction carries the sign meaning (per FR-010 / spec §5.6).

---

## 1. ExtractedStatementResponse *(HTTP 200)*

Root object returned on a successful parse.

| Field | Type | Required | Notes / Source |
|---|---|---|---|
| `statement` | `StatementHeader` | yes | from `Core.Models.Statement` header fields |
| `sections` | `CardholderSection[]` | yes | from `Core.Models.Statement.Sections`, in PDF order |
| `totals` | `StatementTotals` | yes | sums + printed values + reconciliation status |
| `reconciliationStatus` | `string` enum | yes | overall: `"ok"` \| `"needsReview"` |
| `needsReviewCount` | `integer` | yes | count of rows where the reconciler flagged review |
| `unmappedCards` | `string[]` | yes | always **empty `[]`** in this iteration (label resolution out of scope); kept for forward compatibility with the eventual enrichment spec |

### 1.1 StatementHeader

| Field | Type | Required | Notes / Source |
|---|---|---|---|
| `cardType` | `string` | yes | e.g. `"VISA INFINITE BLACK"`; from `Statement.CardType` |
| `maskedAccount` | `string` | yes | e.g. `"4593-78XX-XXXX-2145"`; from `Statement.MaskedAccount` |
| `period` | `StatementPeriod` | yes | issue + cutoff date |
| `pageCount` | `integer` | yes | from `Statement.PageCount` |

### 1.2 StatementPeriod

| Field | Type | Required | Notes |
|---|---|---|---|
| `issueDate` | `string` (ISO date, `YYYY-MM-DD`) | yes | from `StatementPeriod.IssueDate` |
| `cutoffDate` | `string` (ISO date, `YYYY-MM-DD`) | yes | from `StatementPeriod.CutoffDate` |

### 1.3 CardholderSection

| Field | Type | Required | Notes / Source |
|---|---|---|---|
| `cardLast4` | `string` (4 digits) | yes | from `CardholderSection.CardLast4` |
| `rawName` | `string` | yes | from `CardholderSection.RawName` (as appears in PDF) |
| `transactions` | `Transaction[]` | yes | section's rows in PDF order |
| `totals` | `SectionTotals` | yes | computed-from-rows + printed-from-PDF |
| `reconciliationStatus` | `string` enum | yes | `"ok"` \| `"needsReview"` |

### 1.4 Transaction

| Field | Type | Required | Notes / Source |
|---|---|---|---|
| `date` | `string` (ISO date) | yes | transaction date with derived year (FR-006); from `Transaction.TransactionDate` |
| `postingDate` | `string` (ISO date) | yes | from `Transaction.PostingDate` |
| `referenceNumber` | `string` | yes | numeric reference as printed (string preserves leading zeros); from `Transaction.ReferenceNumber` |
| `sequenceCode` | `string` | yes | e.g. `"C011"`, `"X232"`, `"P155"`; from `Transaction.SequenceCode` |
| `rowType` | `string` enum | yes | `"purchase"` \| `"financing"` \| `"payment"` \| `"adjustment"`; from `Transaction.RowType` |
| `description` | `string` | yes | lightly-trimmed raw merchant text; from `Transaction.RawDescription` |
| `amount` | `number` | yes | positive decimal; from `Transaction.Amount` |
| `direction` | `string` enum | yes | `"income"` \| `"expense"`; from `Transaction.Direction` (FR-010: derived from column, never from merchant) |
| `cardLast4` | `string` (4 digits) | yes | redundant with parent section, but convenient; from `Transaction.CardLast4` |
| `needsReview` | `boolean` | yes | `true` if the reconciler flagged this row |
| **Forward-compatibility fields (always `null` in this iteration)** | | | per R4 in `research.md` |
| `categoryId` | `string` (UUID) \| `null` | yes | always `null` here; populated by future enrichment spec |
| `categoryName` | `string` \| `null` | yes | always `null` here |
| `labelId` | `string` (UUID) \| `null` | yes | always `null` here |
| `labelName` | `string` \| `null` | yes | always `null` here |
| `labelUnmapped` | `boolean` | yes | always `false` here (no label resolution attempted) |

### 1.5 SectionTotals

| Field | Type | Required | Notes |
|---|---|---|---|
| `computedCharges` | `number` | yes | sum of `amount` where `direction == expense` in this section |
| `computedCredits` | `number` | yes | sum of `amount` where `direction == income` in this section |
| `printedCharges` | `number` \| `null` | yes | the `$charges` value from the section's `SUBTOTAL.:` line, `null` if not printed |
| `printedCredits` | `number` \| `null` | yes | the `[$credits]` value from the section's `SUBTOTAL.:` line, `null` if not printed |

### 1.6 StatementTotals

| Field | Type | Required | Notes |
|---|---|---|---|
| `computedExpense` | `number` | yes | sum across all sections where `direction == expense` |
| `computedIncome` | `number` | yes | sum across all sections where `direction == income` |
| `printedExpense` | `number` \| `null` | yes | `$charges` from the final `TOTAL ...:` line; `null` if not printed |
| `printedIncome` | `number` \| `null` | yes | `$credits` from the final `TOTAL ...:` line; `null` if not printed |

---

## 2. ExtractionErrorResponse *(HTTP 4xx / 5xx)*

| Field | Type | Required | Notes |
|---|---|---|---|
| `error` | `ErrorBody` | yes | |

### ErrorBody

| Field | Type | Required | Notes |
|---|---|---|---|
| `code` | `string` enum | yes | one of the error codes below |
| `message` | `string` | yes | human-readable message safe to display to the user |

**Error code → HTTP status mapping** (from `research.md` R3):

| `code` | HTTP | Cause |
|---|---|---|
| `INVALID_FILE_TYPE` | 400 | Upload is not a PDF (content-type check + magic-byte sniff `%PDF-`) |
| `EMPTY_FILE` | 400 | `IFormFile.Length == 0` |
| `FILE_TOO_LARGE` | 413 | File exceeds 25 MB cap |
| `PASSWORD_PROTECTED` | 422 | PdfPig reports encrypted document |
| `NO_TEXT_EXTRACTABLE` | 422 | PdfPig opens the document but extracts zero words (scanned PDF) |
| `UNRECOGNIZED_LAYOUT` | 422 | Parser ran but found no cardholder sections AND no transaction rows |
| `PARSE_FAILED` | 500 | Catch-all; details NOT in the response, only in server logs |

---

## 3. Mapping from `CardStatement.Core` → DTOs

Implementation lives in `src/CardStatement.Api/Mapping/StatementMapper.cs`. The mapping is a pure function — no I/O, no DI — so it's trivially unit-testable.

```text
Core.Models.Statement                  → ExtractedStatementResponse
Core.Models.Statement.CardType         → .statement.cardType
Core.Models.Statement.MaskedAccount    → .statement.maskedAccount
Core.Models.Statement.Period           → .statement.period (ISO dates)
Core.Models.Statement.PageCount        → .statement.pageCount
Core.Models.Statement.Sections[*]      → .sections[*]
Core.Models.Statement.TotalIncome      → .totals.computedIncome  (sum cross-check)
Core.Models.Statement.TotalExpense     → .totals.computedExpense (sum cross-check)
Core.Models.Statement.PrintedTotal*    → .totals.printed*        (if exposed by Core; else recompute from Sections)
Core.Models.Statement.ReconciliationStatus → .reconciliationStatus (lowercased)

Core.Models.CardholderSection.CardLast4 → .sections[*].cardLast4
Core.Models.CardholderSection.RawName   → .sections[*].rawName
Core.Models.CardholderSection.Transactions[*] → .sections[*].transactions[*]
Core.Models.CardholderSection.PrintedSubtotals → .sections[*].totals.printed*
Core.Models.CardholderSection.ReconciliationStatus → .sections[*].reconciliationStatus

Core.Models.Transaction.*               → .sections[*].transactions[*].*
   (1:1 except categoryId/labelId/categoryName/labelName are forced to null;
    labelUnmapped is forced to false)
```

**JSON serialization rules**:
- `System.Text.Json` with `JsonSerializerDefaults.Web` (camelCase, case-insensitive on read).
- Enums serialized as lowercase strings via `JsonStringEnumConverter` with `JsonNamingPolicy.CamelCase`.
- Decimals (`amount`, `printed*`) serialized as JSON numbers (not strings) — the existing `result.json` does the same and front-end consumers expect numbers.
- Dates as ISO `YYYY-MM-DD` strings.

---

## 4. Parity with the existing console PoC

The spec's SC-002 requires **row-for-row parity** with the existing console PoC's output for the sample PDF (`/samples/final5140_45178439_316493_0.pdf`). The committed `result.json` at the repo root is that ground truth.

Field-by-field correspondence with `result.json`:

| `result.json` path | DTO path | Notes |
|---|---|---|
| `statement.cardType` | `statement.cardType` | identical |
| `statement.maskedAccount` | `statement.maskedAccount` | identical |
| `statement.period.issueDate` | `statement.period.issueDate` | identical (ISO date) |
| `statement.period.cutoffDate` | `statement.period.cutoffDate` | identical |
| `statement.pageCount` | `statement.pageCount` | identical |
| `totals.income` | `totals.computedIncome` | renamed to make computed-vs-printed explicit |
| `totals.expense` | `totals.computedExpense` | renamed |
| `reconciliationStatus` | `reconciliationStatus` | identical (lowercased enum string) |
| `needsReviewCount` | `needsReviewCount` | identical |
| `unmappedCards` | `unmappedCards` | identical (always `[]` here) |
| `records[*]` | `sections[*].transactions[*]` | **structural difference**: `result.json` flattens transactions; the DTO nests them under their section. Test asserts pass by flattening DTO sections to a `records`-shaped list before comparing. |
| `records[*].date` | `sections[*].transactions[*].date` | identical |
| `records[*].description` | `sections[*].transactions[*].description` | identical |
| `records[*].direction` | `sections[*].transactions[*].direction` | identical |
| `records[*].amount` | `sections[*].transactions[*].amount` | identical |
| `records[*].categoryId` | `sections[*].transactions[*].categoryId` | **forced to `null`** in this iteration (parity test ignores this field) |
| `records[*].categoryName` | `sections[*].transactions[*].categoryName` | **forced to `null`** |
| `records[*].labelId` | `sections[*].transactions[*].labelId` | **forced to `null`** |
| `records[*].labelName` | `sections[*].transactions[*].labelName` | **forced to `null`** |
| `records[*].cardLast4` | `sections[*].transactions[*].cardLast4` | identical |
| `records[*].needsReview` | `sections[*].transactions[*].needsReview` | identical |
| `records[*].labelUnmapped` | `sections[*].transactions[*].labelUnmapped` | **forced to `false`** |

**Net**: The parity test in `ExtractEndpointTests.HappyPath_MatchesGroundTruth` flattens the DTO's `sections[*].transactions` into a `records`-shaped list, nulls out the four enrichment fields and `labelUnmapped` on both sides, and asserts deep equality against `result.json`. New fields (`rowType`, `postingDate`, `referenceNumber`, `sequenceCode`, `printed*`) that don't appear in `result.json` are excluded from the comparison.

---

## 5. State transitions

Not applicable — the response is a snapshot. There is no entity with a lifecycle in this iteration. (Future iterations that introduce persisted statements will need a state model.)

---

## 6. Validation rules (summary, all enforced server-side)

| Rule | Enforced where | Returns |
|---|---|---|
| Single `file` field present | Endpoint binding | 400 `INVALID_FILE_TYPE` if missing |
| `file.Length > 0` | Endpoint | 400 `EMPTY_FILE` |
| `file.Length <= 25 MB` | Kestrel + endpoint | 413 `FILE_TOO_LARGE` |
| File starts with `%PDF-` magic bytes | Endpoint (read first 5 bytes) | 400 `INVALID_FILE_TYPE` |
| PdfPig opens the document | `IPdfExtractor` | 422 `PASSWORD_PROTECTED` on encryption exception |
| PdfPig yields ≥ 1 word | `IPdfExtractor` | 422 `NO_TEXT_EXTRACTABLE` on zero words |
| Parser finds ≥ 1 cardholder section OR ≥ 1 transaction | `IStatementParser` | 422 `UNRECOGNIZED_LAYOUT` if both are zero |
| Any other exception | global filter | 500 `PARSE_FAILED` (details server-logged only) |
