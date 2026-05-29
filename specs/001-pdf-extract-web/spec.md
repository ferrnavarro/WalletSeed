# Feature Specification: PDF Extract & Display (Web MVP)

**Feature Branch**: `001-pdf-extract-web`
**Created**: 2026-05-28
**Status**: Draft
**Input**: User description: "Create the spec to have a api and a frontend app with vite pnpm react. The initial spec will only read the pdf and extract all the information and display it in the frontend app."

## User Scenarios & Testing *(mandatory)*

This MVP delivers the *extraction* slice of the existing PoC over a web surface: a user uploads a BAC Credomatic credit card statement PDF in a browser, the backend deterministically parses it into transactions, and the frontend displays the results in a table. Enrichment (LLM categorization, cardholder labels, reconciliation against external APIs) is **deferred to a later spec** — this iteration intentionally stops at "every row visible and correct".

### User Story 1 - Upload a statement PDF and see every transaction (Priority: P1)

A user opens the web app, selects a BAC Credomatic statement PDF from their computer, and within a few seconds sees a table of every transaction the statement contains — date, posting date, reference number, sequence code, description, amount, and which column (charges vs. credits) the amount came from — grouped by cardholder section as the statement itself groups them.

**Why this priority**: This is the entire MVP. Without it, the product does nothing. It also validates that the parsing engine exposed behind a web API produces the same ground-truth output the existing console PoC already produces against the sample PDF.

**Independent Test**: Upload the sample PDF in `/samples/final5140_45178439_316493_0.pdf` and verify the displayed table matches the rows the existing console parser produces for that file (same count, same per-row values, same section grouping, same direction). Delivers value as a standalone tool even before any enrichment is added.

**Acceptance Scenarios**:

1. **Given** the web app is open and the API is reachable, **When** the user selects a valid BAC Credomatic statement PDF and submits it, **Then** within a short wait the user sees the statement header info (card type, masked account, statement period, page count) and a table of all transactions extracted from the PDF, grouped by cardholder section.
2. **Given** a successfully parsed statement is displayed, **When** the user inspects any row, **Then** that row shows transaction date, posting date, reference number, sequence code, raw description, amount (positive), and direction (income or expense — whether the amount appeared in the credits or charges column).
3. **Given** a statement with multiple cardholder sections that span across page breaks, **When** the parse completes, **Then** every transaction is correctly attributed to the cardholder section it belongs to (by card last-4), including transactions on pages that did not repeat the section header.

### User Story 2 - See per-section and overall totals derived from extracted rows (Priority: P2)

After the table appears, the user can see, per cardholder section and for the whole statement, the sum of charges and the sum of credits (i.e. total expense and total income), computed from the extracted rows. The user can also see the printed subtotals/totals the statement itself prints, side by side, so they can spot extraction errors visually.

**Why this priority**: Totals are how a human eyeballs whether extraction worked. They turn the table from a data dump into something a user can trust at a glance. Lower priority than P1 only because the raw rows must exist first.

**Independent Test**: Upload the sample PDF; verify per-section computed totals match the `SUBTOTAL.:` lines printed in the PDF and run-level totals match the final `TOTAL ...:` line. Mismatches are visibly flagged.

**Acceptance Scenarios**:

1. **Given** a parsed statement is displayed, **When** the user looks at a cardholder section header, **Then** the section shows the computed sum of charges and the computed sum of credits for that section's rows, alongside the subtotal printed in the PDF.
2. **Given** a parsed statement is displayed, **When** the user looks at the statement-level summary, **Then** the user sees total expense and total income computed from the rows and the printed totals from the PDF, with any mismatch between computed and printed values clearly indicated.

### User Story 3 - Get a clear, actionable error when the PDF cannot be parsed (Priority: P3)

If the user uploads a file that is not a BAC Credomatic statement PDF (wrong layout, scanned/image PDF, non-PDF file, corrupt file, password-protected file), the app does not silently produce garbage. It tells the user what went wrong in plain language and does not block them from trying again.

**Why this priority**: The MVP targets a single statement format; without honest error handling, users will mistake "nothing extracted" for "the bank had no transactions". P3 because it does not block the happy path but is required before anyone other than the author can use the tool.

**Independent Test**: Upload (a) a non-PDF file, (b) a scanned/image-only PDF, (c) a PDF from a different bank/layout, (d) a password-protected PDF. Each yields a distinct, non-cryptic error message and leaves the app in a state where the user can immediately try a different file.

**Acceptance Scenarios**:

1. **Given** the user selects a file that is not a PDF, **When** they submit, **Then** the app rejects it before any parse attempt and explains that only PDF files are accepted.
2. **Given** the user uploads a PDF whose layout the parser does not recognize as a BAC Credomatic statement (no transaction table found, or table found but zero rows extracted), **When** the parse completes, **Then** the app shows an explanatory message identifying that the statement format was not recognized, rather than displaying an empty table as success.
3. **Given** the user uploads a scanned/image-only PDF (no extractable text), **When** the parse runs, **Then** the app reports that the PDF contains no machine-readable text and that scanned PDFs are not supported in this version.

### Edge Cases

- **Statement spans a year boundary (Dec → Jan)**: rows in the next calendar year are dated correctly (year derived from the statement period, not assumed from the row).
- **Description collisions / truncations** (e.g. `MASFERRESAN S`, aggregator prefixes like `WOMPI*`, `N1CO*`, `PAGADITO*`): rows are still displayed with the lightly-trimmed raw description; no row is dropped just because its description is noisy.
- **Non-transaction rows in the table band** (`SUBTOTAL.:`, `TOTAL ...:`, `PUNTOS CREDOMATIC`, `ASIGNADOS: ... REDIMIBLE: ...`, `BONIFICACION PAGO ...`): filtered out of the transaction list; never appear as rows.
- **Same person under multiple cards** (e.g. FERNANDO MAGAÑA under `...2706` and `...5468`): each card last-4 produces a separate section; rows are not merged under the name.
- **Section continues across a page break without a repeated header**: rows on the subsequent page are still attributed to the most-recent section, not orphaned.
- **Computed totals disagree with printed totals**: the row table still displays, but the mismatch is visibly flagged so the user knows the extraction is suspect for that statement.
- **Very large file or extremely long parse**: the user is shown a progress/working indicator and is not left wondering whether the app froze; uploads above a sensible size cap are rejected up-front with a clear message.
- **Concurrent uploads in different browser tabs**: each request is independent; results in one tab are not contaminated by another.
- **User uploads the same statement twice**: the second upload produces the same results as the first (extraction is deterministic).

## Requirements *(mandatory)*

### Functional Requirements

**Upload & request flow**

- **FR-001**: The frontend MUST let the user select a single PDF file from their local machine and submit it to the backend for extraction.
- **FR-002**: The frontend MUST reject obviously invalid inputs (non-PDF MIME type / extension, empty file, file above the configured size cap) before sending them to the backend, with a clear in-page error.
- **FR-003**: The backend MUST accept a single PDF in one upload request and return either an extracted-statement result or a structured error explaining why extraction failed.
- **FR-004**: Uploaded PDF bytes MUST NOT be persisted on the server beyond the lifetime of the request; results are returned to the caller and the file is discarded.

**Extraction correctness (parity with the existing console PoC)**

- **FR-005**: The backend MUST parse text-based BAC Credomatic credit card statement PDFs and extract every transaction row from the central transaction table, ignoring overlapping page elements such as account-summary boxes, header banners, and the bottom payment slip.
- **FR-006**: For each transaction the backend MUST extract: transaction date (with year derived from the statement period, handling Dec→Jan rollover), posting date, reference number, sequence code (e.g. `C011`, `X232`, `P155`), the lightly-trimmed raw description, the amount as a positive value, and the direction (income if the amount sat in the credits column, expense if it sat in the charges column).
- **FR-007**: The backend MUST attribute every transaction to the cardholder section it belongs to (identified by card last-4), including transactions on pages where the section header is not repeated, and MUST keep separate sections for separate card last-4 values even when the same human name appears under multiple cards.
- **FR-008**: The backend MUST exclude non-transaction rows from the transaction list: `SUBTOTAL.:`, `TOTAL ...:`, `PUNTOS CREDOMATIC`, `ASIGNADOS: ... REDIMIBLE: ...`, `BONIFICACION PAGO ...`, and any other summary/marketing rows that sit inside the table band.
- **FR-009**: The backend MUST classify each transaction's row type from its sequence-code prefix: `C####` = purchase, `X####` = financing/adjustment, `P####` = payment. Row type MUST be exposed alongside the row but MUST NOT be used to infer direction (direction comes from the column).
- **FR-010**: Direction (income vs. expense) MUST be derived exclusively from which column (CARGOS / charges vs. ABONOS / credits) the amount appears in, never from the merchant string, the sequence-code prefix, or any heuristic over the description.
- **FR-011**: Extraction MUST be deterministic — re-uploading the same PDF MUST produce byte-identical extracted results — and MUST NOT call any external LLM or external API in this iteration.

**Statement-level output**

- **FR-012**: The backend MUST return the statement header information (card type, masked account number, issue date, cutoff date, page count) alongside the transactions.
- **FR-013**: The backend MUST return, per cardholder section and at the statement level, both the values computed by summing the extracted rows (total expense, total income) AND the values printed in the PDF (per-section `SUBTOTAL.:` and final `TOTAL ...:`), so the frontend can show them side by side.
- **FR-014**: The backend MUST report a reconciliation status indicating whether the row-summed totals match the printed totals, both per section and overall. A mismatch MUST NOT cause the request to fail; it MUST be returned as a flagged-but-successful result.

**Frontend display**

- **FR-015**: The frontend MUST display the statement header information at the top of the result view.
- **FR-016**: The frontend MUST display extracted transactions grouped by cardholder section, in the same order they appeared in the statement, with the section's card last-4 and raw cardholder name shown on the section header.
- **FR-017**: Each transaction row in the frontend MUST show, at minimum: transaction date, posting date, reference number, sequence code, description, amount, and a visual indicator of direction (income / expense).
- **FR-018**: The frontend MUST display computed and printed totals together for each section and for the statement as a whole, with any mismatch visibly highlighted.
- **FR-019**: While a parse is in flight, the frontend MUST show a clear "working" indicator and MUST prevent the user from accidentally submitting the same file twice in parallel.
- **FR-020**: When the backend returns a structured error, the frontend MUST surface that error to the user in plain language and return to a state where they can immediately upload another file.

**Out of scope for this iteration (do not implement)**

- LLM-based categorization of transactions.
- Resolution / display of cardholder labels.
- Calls to any external Category, Labels, or wallet API.
- Persistence of extracted statements to any database.
- Authentication, multi-user accounts, sharing, or history of past uploads.
- Export to CSV/JSON download (display only; if desired, will be a separate story).
- Editing, correcting, or annotating extracted rows in the UI.
- Support for banks/layouts other than BAC Credomatic (El Salvador), or for scanned/image PDFs.

### Key Entities

- **Uploaded Statement (request)**: a single PDF file submitted by the user. Has a filename, byte content, and size. Not persisted.
- **Extracted Statement (response root)**: the parsed result of one PDF. Contains the statement header info, an ordered list of cardholder sections, statement-level computed totals, statement-level printed totals, and an overall reconciliation status.
- **Statement Header**: card type (e.g. `VISA INFINITE BLACK`), masked account number, issue date, cutoff date, page count.
- **Cardholder Section**: a contiguous group of transactions belonging to one card. Contains the card last-4, the raw cardholder name as it appears in the PDF, the ordered list of transactions, the section's computed totals, the section's printed subtotal values, and its own reconciliation status.
- **Transaction**: a single extracted row. Has transaction date, posting date, reference number, sequence code, row type (purchase / financing / payment), raw description, amount (positive), direction (income / expense), and the card last-4 of its parent section.
- **Totals Pair**: a (computed-from-rows, printed-in-PDF) pair for both charges and credits, used at both the section and statement level.
- **Reconciliation Status**: per-section and overall; indicates whether computed and printed totals match within tolerance, or are flagged for review.
- **Extraction Error (alternative response)**: a structured error indicating why extraction failed — wrong file type, no extractable text (scanned PDF), unrecognized layout / no transaction table found, password-protected, file too large, or a generic parse failure.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can go from "app open" to "every transaction from the sample BAC statement visible on screen" in **under 10 seconds** for the bundled `/samples/final5140_45178439_316493_0.pdf` on a normal developer laptop with the API running locally.
- **SC-002**: For the sample PDF, the extracted transaction set matches the ground-truth output of the existing console PoC **row-for-row** — same number of transactions, same per-row values for date / description / amount / direction / sequence code / card last-4, and same section grouping.
- **SC-003**: For the sample PDF, computed per-section and statement-level totals match the printed `SUBTOTAL.:` and `TOTAL ...:` values to the cent, and the reconciliation status reports OK.
- **SC-004**: Zero transaction rows displayed in the UI are actually summary or marketing lines (no `SUBTOTAL.:`, `TOTAL ...:`, `PUNTOS CREDOMATIC`, `ASIGNADOS:`, `BONIFICACION PAGO ...` ever appear as transactions).
- **SC-005**: Direction is correct on 100% of rows in the sample (every row's income/expense label matches whether the amount sat in the credits or charges column, verified against the printed statement).
- **SC-006**: Each of the four error categories in User Story 3 (non-PDF, scanned-only PDF, unrecognized layout, password-protected) produces a distinct, human-readable error message and leaves the app immediately usable for another upload.
- **SC-007**: Re-uploading the same PDF in the same session produces byte-identical extracted output (deterministic).
- **SC-008**: A first-time user, given only the app URL and the sample PDF, can produce the transaction view without any instructions beyond what is visible in the UI.

## Assumptions

- **Target user**: the author and a small number of trusted collaborators, working from a desktop or laptop browser. No mobile-first design required; no public-internet deployment in this iteration (localhost / private network is acceptable).
- **Statement format**: only text-based BAC Credomatic (El Salvador) statement PDFs are in scope. Other banks, scanned PDFs, and other BAC layouts are out of scope and may produce an "unrecognized layout" error.
- **One file at a time**: the upload flow is single-file. Batch upload is not in scope.
- **Stateless backend**: the API is stateless — no database, no user accounts, no upload history. PDFs are processed in-memory and discarded when the request completes.
- **No enrichment**: this iteration intentionally does not call the Category API, the Labels API, or any LLM. Those exist in the existing `SPEC.md` and will be layered on in a later spec; the response shape MUST leave room for them later (e.g. `categoryId`/`labelId` may be omitted or null in this iteration) without forcing a breaking change when they are added.
- **Reuse of existing parser logic**: the deterministic PdfPig-based extraction logic already proven in `CardStatement.Core` is the source of truth for parsing. Whether the API reuses that .NET library directly (hosting it behind an HTTP endpoint) or reimplements equivalent behavior in another language is an implementation choice for the planning phase; either way, behavior MUST match the existing console PoC's output for the sample PDF (SC-002).
- **Tech-stack hints from the user**: the frontend is a Vite + React app managed with pnpm, and the system is split into a separate HTTP API and frontend. These choices come from the user's request and are recorded here so the planning phase honors them; the specific API framework/language is not constrained by this spec.
- **Upload size**: a reasonable upload size cap (on the order of 25 MB) is acceptable; statements from the sample bank are well under 5 MB.
- **Browser support**: latest evergreen browsers (Chrome, Firefox, Safari, Edge) on desktop. No IE / legacy browser support.
- **Privacy**: because statements contain personal financial data, the backend MUST NOT log raw PDF bytes or full transaction descriptions at default log levels.
