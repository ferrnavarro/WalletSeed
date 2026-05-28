# Credit Card Statement Parser — Project Specification

## 1. Purpose

A **.NET console application (proof of concept)** that reads a **BAC Credomatic
(El Salvador)** credit card statement PDF, extracts every transaction,
determines whether each is **income** or **expense**, assigns a **category** (via
an LLM constrained to a Category API taxonomy), assigns a **cardholder label**
(via a config-owned card→label map validated against a Labels API), and
**exposes** the enriched records plus **totals of income and expense**.

This PoC only *exposes* the data (in-memory result + JSON/CSV/console output).
Writing transactions back into any external system is **out of scope** for now,
but the design must not preclude adding it later.

Written against a **real sample statement** and the **real responses** of the
Category and Labels APIs (embedded in §7).

> **Note for Claude Code:** The sample PDF is in `/samples`. Read it to verify
> the layout in §5 and to test parser output against ground truth. Use targeted
> page ranges. The PDF is the *test fixture*, NOT the app's runtime extraction
> engine — see §3.

---

## 2. Scope

**In scope (PoC)**
- A **console app** that takes a PDF path and prints/writes the result.
- Parse text-based BAC Credomatic statement PDFs.
- Extract transactions with dates, description, amount, and direction.
- Classify each transaction as **Income** or **Expense**.
- Categorize expenses via LLM ⊂ Category API taxonomy.
- Assign a cardholder label via a config-owned card-last-4 → label-id map.
- Expose enriched records with the exact fields in §8.
- Compute **total income** and **total expense**.
- Validate parsed sections against printed subtotals/totals.

**Out of scope (this PoC)**
- Writing/importing transactions into any external app.
- Web API / UI (console only).
- Scanned/image PDFs and OCR.
- Other banks or other BAC layouts.
- Persistence to a database.
- Automatic name-based card→label matching (mapping is manual config; see §7.4).

---

## 3. Critical architectural decision: extraction vs. categorization

Two distinct "reading" steps — do not conflate them.

1. **Extraction (deterministic, no LLM).** Use **PdfPig** (`UglyToad.PdfPig`)
   to read words *with X/Y coordinates* and reconstruct the transaction table.
   The layout is regular, so coordinate-sorted parsing is reliable, free,
   offline, and enables subtotal reconciliation. **The LLM never extracts.**

2. **Categorization (LLM).** Only short merchant-description strings go to the
   LLM, which picks one category id from the Category API taxonomy. This is the
   only runtime LLM use.

Rationale: the statement is text-based and regular; paying vision/token costs to
extract data PdfPig gets for free — and losing subtotal reconciliation — is the
wrong trade.

---

## 4. Why PdfPig coordinates are required

Raw text extraction interleaves the transaction table with the summary boxes and
the bottom payment slip (overlapping vertically on the page). PdfPig exposes
each word's bounding box, so the parser isolates the transaction table by its
X/Y region (the central table under FECHA / NUMERO DE REFERENCIA / CONCEPTO /
CARGOS / ABONOS) and ignores everything else. A naive text-stream parse pulls in
stray payment-slip numbers and corrupts the data.

---

## 5. The statement format (ground truth)

### 5.1 Document structure
- Multiple pages; each repeats header, account-summary boxes, and bottom payment
  slip. Only the **central transaction table** varies.
- Header shows: card type (`VISA INFINITE BLACK`), masked account
  (`4593-78XX-XXXX-2145`), primary holder, and `PAGINA n/total`.
- Statement period: `FECHA DE EMISION` and `FECHA DE CORTE` (use for year, §5.3).

### 5.2 Cardholder section headers
Transactions are grouped by section; each begins with a header:

```
459378XXXXXX2533    »»» CLAUDIA NAVARRO G
459378XXXXXX2640    »»» FATIMA ORANTES
459378XXXXXX2706    »»» FERNANDO MAGAÑA
459378XXXXXX4941    »»» DAVID MAGANA
459378XXXXXX5468    »»» FERNANDO MAGAÑA
```

Rules:
- Header = masked card `459378XXXXXX####` + `»»»` + name.
- Every row below a header belongs to that card until the next header or a
  `SUBTOTAL.:` line.
- **The card last-4 is the key, not the name.** Same person can hold multiple
  cards and appear in multiple sections (FERNANDO MAGAÑA under `...2706` and
  `...5468`).
- Sections continue across page breaks without repeating the header.

### 5.3 Transaction row grammar
```
ABR/18   19/04   24816301  C011  BURGER KING AHUACHAPAN        $ 2.00
└trans┘  └post┘  └─ref──┘  └seq┘ └────── description ──────┘   └ amt ┘
```
- **Transaction date** `MMM/DD`, Spanish months
  (`ENE FEB MAR ABR MAY JUN JUL AGO SEP OCT NOV DIC`). No year on row — derive
  from statement period; handle Dec→Jan rollover.
- **Posting date** `DD/MM`.
- **Reference number** numeric (`24816301`, `00000605`, `00094000`).
- **Sequence/auth code** letter+digits (`C011`, `X232`, `P155`). Leading letter
  is significant — see §5.4.
- **Description** free text (merchant + branch/city), may be truncated/collided
  (§5.5).
- **Amount** sits in the **CARGOS** column or the **ABONOS** column. **Column
  membership (by X coordinate) sets direction — there is no +/- sign.**

### 5.4 Row types (classify by sequence-code prefix + content)
- **`C####` — purchase** → Expense. Goes to categorization.
- **`X####` — financing / adjustment** (`PLAN PRF`, `REVERSION PLAN PRF`,
  `T.ADI`/`T.TIT`). Direction depends on column: a charge `PLAN PRF` is an
  Expense; a `REVERSION ...` in ABONOS is Income/credit. NOT a merchant — do not
  send to merchant categorizer; assign a fixed category (see §6.4).
- **`P####` — payment** (`SU PAGO RECIBIDO GRACIAS`) → in ABONOS → Income/credit.
  NOT a merchant; fixed category (see §6.4).
- **Filter out (never transactions):** `SUBTOTAL.:`, `TOTAL ...:`,
  `PUNTOS CREDOMATIC`, `ASIGNADOS: ... REDIMIBLE: ...`, `BONIFICACION PAGO ...`.

### 5.5 Description quirks (must handle)
- Branch/city appended and often truncated/collided:
  `BURGER KING AVE. MASFERRESAN S` = "...MASFERRER" + "SAN S";
  trailing `SAN S` / `LA LI` / `ANTIG` are truncated regions.
- Aggregator prefixes: `WOMPI*WENDYS`, `N1CO*SELECTOS`, `PAGADITO*SISA`.
- Do not over-clean with rules; pass the lightly-trimmed raw string to the LLM,
  which is robust to this noise.

### 5.6 Direction (Income vs Expense) — definitive rule
- **Expense** = amount in the **CARGOS** column (purchases, fees, financing
  charges).
- **Income** = amount in the **ABONOS** column (payments `P####`, reversals
  `REVERSION`, any credit).
- This is the single source of truth for the income/expense field. Do NOT infer
  it from the merchant or category.

### 5.7 Built-in validation (use it)
- Each section prints `SUBTOTAL.: $charges [$credits]`.
- Final `TOTAL ...: $charges $credits`.
- Sum parsed rows per section/direction and reconcile against printed values.
  On mismatch, flag `NeedsReview` — surface it, don't fail silently. The app's
  computed totals (§8) should match printed `TOTAL` charges/credits when
  reconciliation passes.

---

## 6. Pipeline & architecture

```
CreditStatementParser.sln
├── CardStatement.Core            domain models + interfaces, no deps
├── CardStatement.Pdf             PdfPig extraction (words + coordinates)
├── CardStatement.Parsing         words → Transaction records (BAC grammar)
├── CardStatement.Categorization  LLM categorizer + Category API client
├── CardStatement.Labels          Labels API client + config card→label map
├── CardStatement.App             console app: orchestration + output
└── CardStatement.Tests           unit + golden-file tests vs the sample
```

### 6.1 Console app behavior (App)
- Invocation: `CardStatement.App <path-to-pdf> [--out result.json] [--csv result.csv]`.
- Reads config (`appsettings.json` + secrets), runs the pipeline, prints a
  summary to the console, and writes the JSON (and optional CSV) result.
- Exit non-zero if parsing fails hard; exit zero with warnings if
  reconciliation/label issues are only flagged.

### 6.2 Flow
```
PDF
 → PdfExtractor (PdfPig): pages → words+coords
 → TableLocator: isolate central transaction-table X/Y band per page
 → RowBuilder: cluster words into rows by shared Y, order by X
 → StatementParser: classify rows; track current section/card; set Direction
                    by column; capture printed subtotals/total
 → Reconciler: parsed sums vs printed → flag NeedsReview
 → LabelResolver: card last-4 → label id (config map) → validate vs Labels API
 → Categorizer: Expense purchases (batched) → category id (LLM ⊂ taxonomy);
                fixed categories for payments/financing (§6.4)
 → ResultBuilder: enriched records (§8) + total income + total expense
 → Output: console summary + JSON (primary) / CSV (optional)
```

### 6.3 Domain models (Core)
- `Statement` — card type, masked account, period, page count, `Sections`,
  `TotalIncome`, `TotalExpense`, `ReconciliationStatus`.
- `CardholderSection` — `CardLast4`, `RawName`, `LabelId?`, `LabelName?`,
  `Transactions`, printed subtotals, `ReconciliationStatus`.
- `Transaction` — `TransactionDate`, `PostingDate`, `ReferenceNumber`,
  `SequenceCode`, `RowType` (Purchase|Financing|Payment|Adjustment),
  `RawDescription`, `Amount` (decimal, positive), `Direction` (Income|Expense),
  `CardLast4`.
- `EnrichedRecord` — the exposed shape in §8.
- `Category` — `Id` (guid), `Name`, `Color`, `EnvelopeId`, `Cardinality?`.
- `Label` — `Id` (guid), `Name`, `Color`, `Archived`.

### 6.4 Fixed (non-LLM) categories
Payments and financing/adjustment rows are not merchants and must not go to the
LLM. Map them to fixed category ids resolved from the taxonomy by name at
startup (configurable). Suggested defaults (confirm against how the finance app
uses them):
- Payments (`P####`, `SU PAGO RECIBIDO`) → "Debt" or a dedicated payment category.
- Financing/reversal (`X####`, `PLAN PRF`, `REVERSION`) → "Loan, interests" or
  "Refunds (tax, purchase)" by direction.
- Unmatched/low-confidence → "Automatic bank statements reading" (`40b565bb-…`)
  as the fallback bucket.

---

## 7. External APIs

All APIs require a **Bearer token**.

**For testing/PoC purposes, the API base URL and bearer token are placed in
`appsettings.json`** so the app can call the live endpoints during development.
The token must NOT be committed to source control — keep the real value in
`appsettings.Development.json` (git-ignored) or user-secrets/env vars, with a
placeholder in the committed `appsettings.json`. Never put the token in a URL
query string. All clients send `Authorization: Bearer <token>`.

```json
// appsettings.json (committed — placeholder values)
"Api": {
  "BaseUrl": "https://<host>",          // e.g. https://host/v1/api
  "BearerToken": "REPLACE_VIA_SECRETS"  // real value in Development/secrets
}
```

Clients may still be **stubbed from the embedded samples below** for offline unit
tests, but live calls are configured via the above for PoC testing.

### 7.1 Category API — `GET /v1/api/categories`
- **Paginated.** Response includes `limit`, `offset`, `nextOffset`, and an
  `agentHints` entry of type `pagination.has_more` whose `action.url` gives the
  next page (e.g. `?agentHints=true&limit=30&offset=30`). The client MUST follow
  pages until exhausted and assemble the full taxonomy.
- Fields: `id` (guid), `name`, `color`, `envelopeId`, optional `cardinality`
  (`mus`|`want`), optional `iconName`, plus `custom*`/timestamps.
- **Key by `id` (guid).** `envelopeId` is NOT unique (e.g. `6009` covers
  Holiday/Flights/Travel-misc; `2004`, `6001` repeat). Never key by it.
- **Skip empty names.** The category `3df5bc6d-…` has `"name": ""` — exclude it
  from the set offered to the LLM.
- Cache for the run.

**Sample response (page 1, `offset=0&limit=30`):**
```json
{
  "agentHints": [
    {
      "type": "pagination.has_more",
      "severity": "instruction",
      "action": { "url": "/wallet/v1/api/categories?agentHints=true&limit=30&offset=30" },
      "text": "More records available at next page"
    }
  ],
  "categories": [
    { "id": "0304e8b3-8b91-4820-86f2-1d8fe9d9cb1f", "color": "#64DD17", "name": "Laundry", "cardinality": "mus", "customCategory": true, "customColor": false, "customName": true, "envelopeId": 6001, "iconName": "t-shirt-filled" },
    { "id": "0377474e-dcbc-487d-9241-55db8b46d5ef", "color": "#4FC3F7", "name": "Jewels, accessories", "customCategory": false, "customName": false, "envelopeId": 2001 },
    { "id": "041e43d7-6a9c-4acc-b877-29ceb0811fe4", "color": "#FF3D00", "name": "Groceries", "customCategory": false, "customName": false, "envelopeId": 1000 },
    { "id": "08072012-4fb3-4418-8541-694f06ad3ae9", "color": "#1565c0", "name": "OneClick", "customCategory": false, "customName": false, "envelopeId": 20004 },
    { "id": "081c7251-e921-4b03-ab9e-70c8c62bd6f4", "color": "#FBC02D", "name": "Lending, renting", "customCategory": false, "customColor": false, "customName": false, "envelopeId": 10005 },
    { "id": "0ba25396-1fd3-477d-9b73-100a4229942b", "color": "#FBC02D", "name": "Refunds (tax, purchase)", "customCategory": false, "customColor": false, "customName": false, "envelopeId": 10008 },
    { "id": "0be83190-0d31-4455-a92b-540cfc2c6e98", "color": "#64DD17", "name": "Education, development", "customCategory": false, "customName": false, "envelopeId": 6006 },
    { "id": "10c12b30-bf94-4b58-bac8-3e2e4528feb6", "color": "#26c6da", "name": "Debt", "customCategory": false, "customName": false, "envelopeId": 20000 },
    { "id": "11579b86-aa4a-456d-b384-4e307f3e14fa", "color": "#4FC3F7", "name": "Travel shopping", "cardinality": "want", "customCategory": true, "customColor": false, "customName": false, "envelopeId": 2009, "iconName": "shopping-bag-filled" },
    { "id": "13d15950-4a07-4260-a3ec-e828871c9098", "color": "#00BFA5", "name": "Insurances", "customCategory": false, "customName": false, "envelopeId": 8001 },
    { "id": "151201e4-339b-4943-b9c3-fd44a2cad43c", "color": "#4FC3F7", "name": "Kitchen", "cardinality": "want", "customCategory": true, "customColor": false, "customName": false, "envelopeId": 2004, "iconName": "restaurant-filled" },
    { "id": "1b60c34d-ace4-4043-8aaa-51f85f890ed1", "color": "#FF4081", "name": "Investments", "customCategory": false, "customColor": false, "customName": false, "envelopeId": 9005 },
    { "id": "1eead86a-e9b6-4780-9131-aff50bcbdcb3", "color": "#64DD17", "name": "Holiday, trips, hotels", "customCategory": false, "customColor": true, "customName": false, "envelopeId": 6009, "iconName": "beach-filled" },
    { "id": "1efd8477-3fc8-41c7-ab6c-db18e2ba2d35", "color": "#AA00FF", "name": "Rentals", "customCategory": false, "customColor": false, "customName": false, "envelopeId": 5003 },
    { "id": "21de4ca0-1706-40ac-aead-0ebe8f95dc32", "color": "#536DFE", "name": "Communication, PC", "customCategory": false, "customName": false, "envelopeId": 7005 },
    { "id": "269b411f-bc7c-43d9-a87a-c6b24c8b9919", "color": "#FFA726", "name": "Prima", "cardinality": "mus", "customCategory": true, "customColor": false, "customName": false, "envelopeId": 3001, "iconName": "exterior-filled" },
    { "id": "2ce2031b-d1e7-4776-9620-405e20c2a9c6", "color": "#FFA726", "name": "Maintenance, repairs", "customCategory": false, "customName": false, "envelopeId": 3004 },
    { "id": "2dcb851c-09de-4bc8-9f71-06c18a99452c", "color": "#64DD17", "name": "Flights", "cardinality": "want", "customCategory": true, "customColor": false, "customName": false, "envelopeId": 6009, "iconName": "airplane-mode-on-filled" },
    { "id": "30ebeb9f-b5ab-4b55-bb62-562fd6ad2b7c", "color": "#64DD17", "name": "Wellness, beauty", "customCategory": false, "customName": false, "envelopeId": 6001 },
    { "id": "34099521-af5e-4966-9fdf-c8d152cf55e7", "color": "#64DD17", "name": "Health care, doctor", "customCategory": false, "customName": false, "envelopeId": 6000 },
    { "id": "353a4dc4-07bc-4d84-906a-ae346dbe209a", "color": "#4FC3F7", "name": "Health and beauty", "customCategory": false, "customName": false, "envelopeId": 2002 },
    { "id": "39128661-b4c1-4dd6-83c1-bc8d8791c8d3", "color": "#64DD17", "name": "Life events", "customCategory": false, "customName": false, "envelopeId": 6004 },
    { "id": "3a4b7d73-7100-4dab-a6e4-912f6051fe4b", "color": "#4FC3F7", "name": "Clothes & shoes", "customCategory": false, "customName": false, "envelopeId": 2000 },
    { "id": "3aea0a15-d1b5-46fd-9965-98868dd410ca", "color": "#00BFA5", "name": "Loan, interests", "customCategory": false, "customName": false, "envelopeId": 8002 },
    { "id": "3df5bc6d-4c6d-40ae-8f0f-23ed3e35f810", "color": "#cccccc", "name": "", "customCategory": false, "customName": false, "envelopeId": 2004 },
    { "id": "4066fdc1-817f-4275-89b9-bc7e62083b55", "color": "#64DD17", "name": "Travel miscellaneous", "cardinality": "want", "customCategory": true, "customColor": false, "customName": false, "envelopeId": 6009, "iconName": "check-book-filled" },
    { "id": "406771b4-a535-48ea-8f72-712a2d2c979d", "color": "#FFA726", "name": "Rent", "customCategory": false, "customColor": false, "customName": false, "envelopeId": 3000 },
    { "id": "40b565bb-d9cc-430a-a4ef-0c8649b636ab", "color": "#cccccc", "name": "Automatic bank statements reading", "customCategory": false, "customName": false, "envelopeId": 20005 },
    { "id": "43de9993-e214-4379-a633-7e66d11f2259", "color": "#AA00FF", "name": "Leasing", "customCategory": false, "customName": false, "envelopeId": 8006 },
    { "id": "447a5f1b-6950-455a-8cce-f8c9c9463577", "color": "#AA00FF", "name": "Fuel", "customCategory": false, "customName": false, "envelopeId": 5000 }
  ],
  "limit": 30,
  "nextOffset": 30,
  "offset": 0
}
```
*(This is page 1 of N — the client must follow `nextOffset` for the rest. Note
restaurant/fast-food categories like BURGER KING may appear on a later page; if
no dining category exists, "Kitchen" (icon `restaurant-filled`) is the closest.)*

### 7.2 Labels API — `GET /v1/api/labels`
- Returns user-defined tags `{ id (guid), name, color, archived, timestamps }`.
  Has `limit`/`offset` (no agentHints in sample) — page defensively.
- **Free-form tags, not a cardholder directory.** Mixes cardholder labels
  (`BAC Titular`, `BAC adicional(David)`, `BAC adicional (Fátima)`,
  `BAC adicional (Mamá)`), project tags (`Coderia`, `Europa 2023`, `Hipoteca`,
  `Home`, `Compras C71`), and emoji/mood tags.
- **Filter out `archived: true`.**
- The API has NO card numbers; resolution is via §7.4. This API only validates
  configured label ids and powers a first-time mapping proposal.

**Sample response (`offset=0&limit=30`):**
```json
{
  "labels": [
    { "id": "0ba602db-f5fb-4f71-8f44-7fc0af708856", "archived": false, "color": "#43A047", "name": "Hipoteca" },
    { "id": "16aa3eb4-e545-47d2-a45a-135b3475ac81", "archived": false, "color": "#212121", "name": "BAC adicional(David)" },
    { "id": "1e33d881-1563-4900-9c8f-5800c0e810e6", "archived": false, "color": "#5c000000", "name": "💩" },
    { "id": "1f3b6f74-fc08-497a-a7d4-a89d1b6e581c", "archived": false, "color": "#5c000000", "name": "😐" },
    { "id": "34a53db5-89c9-4749-8454-485e758932af", "archived": false, "color": "#AD1457", "name": "Europa 2023" },
    { "id": "48108c9f-4084-4c17-8fc2-08bf4380a220", "archived": false, "color": "#5c000000", "name": "💗️️" },
    { "id": "6b920d11-4066-4296-ab23-df1a977ac7dd", "archived": false, "color": "#D32F2F", "name": "FF food" },
    { "id": "7c4fe378-882a-49b2-b7de-3fb076694a01", "archived": false, "color": "#212121", "name": "BAC adicional (Fátima)" },
    { "id": "7ef20d7d-4287-4ea4-aff1-c7560d3e7354", "archived": false, "color": "#6099EB", "name": "Home" },
    { "id": "936a90c7-01c4-4bf4-805a-59733a925547", "archived": false, "color": "#212121", "name": "BAC Titular" },
    { "id": "ab21ecf2-44ef-4aa8-8088-bb802d619bcc", "archived": false, "color": "#FF6F00", "name": "Coderia" },
    { "id": "af537eaf-0555-437a-a9f4-90329093f73f", "archived": false, "color": "#FF1744", "name": "Compras C71" },
    { "id": "bfe6901c-6c5c-46ea-a996-ecd281e67de7", "archived": false, "color": "#ec407a", "name": "Lola 🐾" },
    { "id": "c049554c-b118-4e47-9aa5-9f863507cfeb", "archived": false, "color": "#212121", "name": "BAC adicional (Mamá)" }
  ],
  "limit": 30,
  "offset": 0
}
```

### 7.3 Endpoints summary
| API | Method | Path | Paginated | Auth |
|---|---|---|---|---|
| Categories | GET | `/v1/api/categories` | yes (agentHints + nextOffset) | Bearer |
| Labels | GET | `/v1/api/labels` | yes (limit/offset) | Bearer |

### 7.4 Card → Label mapping (config-owned, source of truth)
- Manual mapping in `appsettings.json`, keyed by **card last-4 → label id**
  (guid). Per-card, so the same person under two cards maps independently, and
  two cards can point to one label.
```json
"CardholderLabels": {
  "2533": "c049554c-b118-4e47-9aa5-9f863507cfeb",
  "2640": "7c4fe378-882a-49b2-b7de-3fb076694a01",
  "2706": "936a90c7-01c4-4bf4-805a-59733a925547",
  "4941": "16aa3eb4-e545-47d2-a45a-135b3475ac81",
  "5468": "936a90c7-01c4-4bf4-805a-59733a925547"
}
```
- At startup, validate every configured label id exists in the Labels API and is
  not archived; warn on any that don't.
- **Unmapped card:** if a section's card last-4 is absent from config, do NOT
  guess. Emit its transactions with `LabelId = null`, mark them `LabelUnmapped`,
  and surface the new card last-4 + raw name in the run summary.

---

## 8. Output contract (what this PoC exposes)

In-memory result serialized to **JSON** (primary), optional **CSV**, plus a
**console summary**. Each enriched record exposes exactly:

| Field | Type | Source |
|---|---|---|
| `date` | date (ISO `yyyy-MM-dd`) | transaction date + derived year |
| `description` | string | raw (lightly trimmed) merchant description |
| `direction` | `"income"` \| `"expense"` | CARGOS vs ABONOS column (§5.6) |
| `amount` | decimal (positive) | parsed amount |
| `categoryId` | guid \| null | LLM (purchases) / fixed (§6.4) / null |
| `categoryName` | string \| null | resolved from taxonomy by id |
| `labelId` | guid \| null | config card→label map (null if unmapped) |
| `labelName` | string \| null | resolved from Labels API by id |

Plus run-level totals:
- `totalIncome` — sum of `amount` where `direction == income`.
- `totalExpense` — sum of `amount` where `direction == expense`.
- Recommended also: `reconciliationStatus`, `LabelUnmapped` count, uncategorized
  / `NeedsReview` count.

Notes:
- `amount` is positive; `direction` carries the sign meaning (derive signed
  later if needed: expense = negative).
- Totals must match printed `TOTAL ...:` charges/credits when
  `reconciliationStatus == OK`.

---

## 9. Categorization rules (LLM)
- Only `Direction == Expense` && `RowType == Purchase` rows go to the LLM.
- Pass allowed categories (id + name, empty names removed) in the prompt;
  instruct the model to return exactly one **id** per transaction.
- **Validate** each returned id is in the allowed set; if not → null +
  `NeedsReview` (or fallback bucket §6.4). Never accept invented ids.
- Batch 20–50 per call; align results by index or reference number.
- Low temperature. `ILlmClient` provider-agnostic.
- Payments/financing get fixed categories (§6.4), not the LLM.

---

## 10. Configuration (`appsettings.json`)
- `Api.BaseUrl`, `Api.BearerToken` — for PoC testing (token via Development
  override / secrets / env var; placeholder in committed file). See §7.
- LLM provider + model + key (key via secrets/env).
- `CardholderLabels` map (§7.4).
- Fixed-category name mappings (§6.4).
- Batch size, table X/Y band tolerances, default input/output paths.

---

## 11. Testing
- **Golden-file test:** sample PDF → expected enriched JSON (records + totals).
- Unit tests: row classification (C/X/P), date parsing incl. Dec→Jan rollover,
  description collision (`MASFERRESAN S`), Income/Expense by column, subtotal &
  total reconciliation (force mismatch → NeedsReview), unmapped card →
  LabelUnmapped, out-of-taxonomy id rejected, total income/expense math.
- Categorizer test with fake `ILlmClient` returning fixed ids.
- API client tests against the embedded samples in §7 (categories pagination).

---

## 12. Build order
1. Core models + interfaces.
2. PdfPig extractor + table locator + row builder; prove on sample.
3. StatementParser (BAC grammar) + Direction logic + Reconciler; golden-file green.
4. Category API client (pagination) + Labels API client (stub from §7 samples).
5. Config card→label map + validation + unmapped handling.
6. Categorizer + provider-agnostic LLM client (stub LLM first, then real).
7. ResultBuilder (enriched records + totals) + console summary + JSON/CSV.
8. (Later) write-back; persistence; web API.

---

## 13. Non-negotiables / gotchas checklist
- [ ] PoC is a console app; PDF path in, JSON/CSV/console out.
- [ ] Isolate the transaction table by coordinates — don't parse the raw stream.
- [ ] Income vs Expense = ABONOS vs CARGOS column, never inferred from merchant.
- [ ] `amount` positive; `direction` carries the meaning.
- [ ] Attribute by card last-4; sections continue across pages.
- [ ] `X####` = financing/adjustment, `P####` = payment — neither is a merchant.
- [ ] Derive year from statement period; handle Dec→Jan rollover.
- [ ] Category API paginated (follow nextOffset); key by guid, not envelopeId.
- [ ] Skip empty-named categories; reject invented category ids from the LLM.
- [ ] Labels are free-form tags; filter archived; card→label is config-owned.
- [ ] Unmapped card → null label + LabelUnmapped flag, surfaced.
- [ ] Reconcile parsed sums vs printed SUBTOTAL/TOTAL; flag mismatches.
- [ ] Base URL + bearer token in appsettings for PoC testing; token never committed.
- [ ] totalIncome / totalExpense from direction; match printed TOTAL when OK.
