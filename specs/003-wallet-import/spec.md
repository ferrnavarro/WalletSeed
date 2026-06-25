# Feature Specification: Wallet Import from BAC PDF Statements

**Feature Branch**: `003-wallet-import`
**Created**: 2026-06-25
**Status**: Draft
**Input**: User description: "Upload BAC PDF statements to a Wallet budget tracker via its API. Goal: extract records from the PDF, compare them against records already in Wallet to prevent duplicates (date within 1–2 days + same amount), let the user pick which extracted records to push, then create those records via the Wallet API. The frontend shows PDF records side-by-side with the Wallet records found in the same window so the user can decide what to upload."

## User Scenarios & Testing *(mandatory)*

The existing app already extracts a structured statement from an uploaded BAC PDF (sections, transactions, totals). This feature adds the **next step**: comparing those extracted transactions against what already lives in the user's Wallet budget tracker (an external HTTP API), and pushing the ones the user wants to keep. The work happens on a **new "Wallet Import" page** in the existing frontend (separate from the current "Statement Extract" page) and runs end-to-end on that single page: upload PDF → see PDF rows vs. existing Wallet rows for the same date window → choose which extracted rows to import + assign a category per row → push the chosen rows to Wallet → see a per-row outcome.

A single Wallet account is picked for the whole import (BAC credit cards in Wallet are tracked as one account each). Labels are applied automatically from a backend-side mapping that translates each BAC cardholder section (the per-cardholder "subcards" inside one statement) into a Wallet label id. Category is the only per-row pick the user makes for each imported row. `paymentType` is fixed to `credit_card` for this iteration because BAC statements are credit-card statements. The Wallet JWT is read by the backend from local configuration; the browser never sees it. All Wallet API calls go through the existing .NET backend as a proxy, so the frontend talks only to our own backend.

### User Story 1 - See PDF rows next to existing Wallet rows for the same period, with duplicate matches highlighted (Priority: P1)

A user opens the Wallet Import page, picks a Wallet account from a dropdown of the accounts the Wallet API returns, uploads a BAC PDF, and sees a single side-by-side view: every transaction extracted from the PDF on one side, and every transaction already in the chosen Wallet account that falls within the PDF's statement period (plus a small buffer) on the other side. Each PDF row is annotated with whether the system thinks it is already in Wallet — a "likely duplicate" annotation that points at the matching Wallet row(s), based on **date within ±2 days and exact same amount**. Each Wallet row shows whether anything in the PDF claims to match it.

**Why this priority**: This is the entire reason the feature exists. Even if the "push" step from US3 never runs, the user already gets value: an honest answer to "did my manual entries for this BAC card cover this statement, and if not, what's missing?". Without this view the user has nothing to base a decision on, so every other story is gated on it being right.

**Independent Test**: Upload a BAC PDF whose statement period is known. The page must (a) load the existing Wallet records for the picked account whose `recordDate` falls within the statement period extended by a small buffer on both ends, (b) display them next to the PDF rows, (c) annotate which PDF rows have a likely-duplicate Wallet row by the documented match rule (±2 days + exact amount + same currency + same direction — expense vs. income), and (d) annotate which Wallet rows were claimed by a PDF row. No "push" action is required for this test to demonstrate value.

**Acceptance Scenarios**:

1. **Given** a chosen Wallet account and a freshly-uploaded BAC PDF, **When** the comparison view loads, **Then** every transaction returned by the PDF extractor appears in the "PDF rows" column with its date, signed amount (expense = negative, income = positive), currency, description, and source cardholder section.
2. **Given** the same view, **When** it loads, **Then** every existing Wallet record on the chosen account whose `recordDate` falls within the PDF's statement period extended by the documented buffer (±5 days at each boundary) appears in the "Wallet rows" column with its date, signed amount, currency, note, counter-party, and category.
3. **Given** a PDF row and a Wallet row whose dates are within ±2 calendar days of each other AND whose amounts are equal in value and currency AND whose direction matches (both expenses, or both incomes), **When** the view renders, **Then** both rows are visually paired and the PDF row carries a "likely duplicate" indicator pointing at the matched Wallet row.
4. **Given** a PDF row that matches **more than one** Wallet row by the rule (e.g. two identical Wallet entries at $25.00 within the window), **When** the view renders, **Then** the indicator on the PDF row makes clear that it claims multiple Wallet rows and lists them, and each candidate Wallet row is marked as claimed.
5. **Given** a Wallet row that no PDF row claims, **When** the view renders, **Then** that Wallet row is shown with no "matched" annotation (so the user can see manual-entry rows that the PDF doesn't account for).
6. **Given** the comparison view, **When** the user re-uploads the same PDF in the same session, **Then** the same rows, the same pairings, and the same indicators appear in the same order (the view is deterministic).

---

### User Story 2 - The user picks which extracted rows to push and supplies the per-row category (Priority: P1)

From the comparison view, the user selects which PDF rows should become new Wallet records. Defaults: rows that the system flagged as "likely duplicate" start **unselected**; all other rows start **selected**. The user can flip any row's selection. For every selected row the user must pick a category from the list of Wallet categories the backend has returned. paymentType is fixed to `credit_card`. labels are applied automatically by the backend per the cardholder-section → label-id mapping in its configuration. A row that has no category picked cannot be submitted. The user sees a running count of selected rows and the per-row Wallet payload that *will* be sent (account, date, signed amount, currency, paymentType, category name, automatic label names, description note, counter-party if known) so there are no surprises at submit time.

**Why this priority**: Same priority as US1 because the two together are the smallest end-to-end product that delivers value. Without selection + category assignment the user cannot do the only thing the feature is here to enable, namely safely importing into Wallet. Splitting US1 and US2 across releases would ship a useful diagnostic view without a way to act on it.

**Independent Test**: With the comparison view loaded, the user toggles a few rows, picks categories on the selected ones, and inspects the per-row preview of the Wallet payload that would be POSTed. No actual POST happens in this test. The test asserts: (a) likely-duplicates start off, (b) others start on, (c) toggling persists in the page until submit, (d) a selected row with no category disables the submit action, (e) the preview shows the exact fields documented in the Functional Requirements (account, date, signed amount, currency, paymentType, categoryName, labelNames, note, counterParty).

**Acceptance Scenarios**:

1. **Given** the comparison view, **When** it first renders, **Then** PDF rows annotated as "likely duplicate" have their selection unchecked by default, and PDF rows with no duplicate match have their selection checked by default.
2. **Given** the user clicks a selected row's checkbox, **When** the click is processed, **Then** the row toggles between selected and unselected and the "Selected: N" counter updates.
3. **Given** any selected row that does not yet have a category picked, **When** the user looks at the submit control, **Then** submit is disabled and the offending row(s) are marked as "needs category".
4. **Given** every selected row has a category, **When** the user opens the per-row preview, **Then** for each selected row the preview shows the literal fields that will be sent: `accountId` (display: account name), `recordDate` (ISO), `amount.value` (signed), `amount.currencyCode`, `paymentType = credit_card`, `categoryId` (display: category name), `labelIds` (display: label names from the cardholder-section mapping, or "(no label)" if the cardholder section has no mapping), `note` (the BAC description), `counterParty` (the BAC merchant if extractable).
5. **Given** a selected row whose source cardholder section has no entry in the backend label mapping, **When** the preview renders, **Then** the row is still submittable and the preview makes the missing mapping visible to the user (so the user knows the row will land with no label, on purpose).

---

### User Story 3 - Push the selected rows to Wallet, with a per-row outcome (Priority: P1)

When the user clicks "Import to Wallet", the backend posts the selected rows to the Wallet API in a single batched call (Wallet supports batches up to 50 per request; the backend chunks larger selections behind the scenes) and shows a per-row outcome: success (with the new Wallet record id) or failure (with a human-readable error message). The view then makes it clear which rows ended up in Wallet and which did not, and offers a single "Reload comparison from Wallet" action that re-fetches Wallet for the same period so the user can verify by eye that the imported rows are now there.

**Why this priority**: This is the action the user came to perform. P1 alongside US1 and US2 because together they are the smallest shippable slice that earns the feature its name. Splitting US3 off as P2 would mean shipping a comparison-and-staging tool that does not actually import — useless given the explicit goal.

**Independent Test**: With a comparison view loaded and a small number of rows selected and categorized, click "Import to Wallet". The test asserts: (a) the backend issues one or more Wallet `POST /v1/api/records` calls covering all selected rows in chunks of at most 50, (b) each row's outcome (success/failure) is reflected in the UI within a few seconds, (c) a partial failure (some 200, some per-item failures) does not roll the whole batch back — successful rows remain in Wallet, failed rows are marked with their errors and stay available for retry, (d) clicking "Reload comparison from Wallet" re-fetches and the previously imported rows now appear on the Wallet side.

**Acceptance Scenarios**:

1. **Given** the user has selected N rows (all with categories), **When** the user clicks "Import to Wallet", **Then** the backend submits all N rows to Wallet in batched POST calls of at most 50 rows each, in the order they appear on screen, and reports the result per row.
2. **Given** Wallet returns a mixed-outcome batch (HTTP 207 with some successes and some failures), **When** the response is processed, **Then** each row in the UI shows its individual outcome — successful rows show a check plus the new Wallet record id; failed rows show the row-level error message Wallet returned and remain selectable for retry.
3. **Given** Wallet returns 401 Unauthorized (the configured JWT is missing or expired), **When** the response is processed, **Then** no row is marked successful, the page shows a single banner with a clear "the backend's Wallet credentials are not valid; ask the operator to refresh them" message, and the row selection state is preserved.
4. **Given** the import completes (even partially), **When** the user clicks "Reload comparison from Wallet", **Then** the Wallet column re-fetches from the API and the rows that were successfully imported in step 1 now appear in the Wallet column and pair with the previously-imported PDF rows under the same duplicate rule.
5. **Given** an in-flight import, **When** the user navigates away or refreshes the page, **Then** rows already accepted by Wallet remain in Wallet (no rollback) and the next visit shows them on the Wallet side — there is no claim that import is transactional across the whole selection.

### Edge Cases

- **Wallet account currency vs. PDF row currency mismatch**: BAC statements can carry transactions in a currency other than the account's primary currency (e.g. a USD card showing a $-denominated row alongside a colón row). A PDF row whose currency does not match the chosen Wallet account's currency MUST be visibly flagged in the comparison view and MUST default to unselected, so the user explicitly opts in if they want to push it. Wallet's own API enforces currency match against the account; the backend does not silently rewrite currencies.
- **Wallet account has zero records in the window**: the Wallet column shows "No existing records in this period" rather than an empty silent panel, so the user is not left guessing whether the API call failed.
- **Wallet returns more than one page of records**: the backend MUST fetch all pages of records covering the date window before the comparison view is shown — the user never sees a partial "first page only" comparison, and "no duplicates found" actually means no duplicates found anywhere in the window.
- **The PDF's statement period cannot be determined** (e.g. the extractor returned a statement with no header dates): the user is told the system cannot determine a comparison window and is asked to confirm a date range manually before the comparison loads. The feature does not silently fall back to "the last 90 days" or similar.
- **Two PDF rows look identical** (same date, amount, currency, direction — e.g. two $5.00 expenses on the same day): each PDF row is matched independently against Wallet; both can claim the same Wallet row if there is only one matching Wallet record. The view makes this visible (both PDF rows show "matched to W-123") so the user can decide whether to import one, both, or neither.
- **A row's category is later renamed or deleted in Wallet between when the page loaded and when the user clicks import**: the import call fails for that row with the Wallet error message surfaced verbatim; other rows in the batch still attempt to go through. The page does not silently re-fetch categories mid-flow.
- **The backend's configured JWT is missing at startup** (e.g. the operator forgot to set the config value): the Wallet Import page MUST refuse to load the comparison and MUST show an operator-friendly message ("Wallet credentials not configured"), not a 500. The existing "Statement Extract" page (which does not use Wallet) MUST keep working — Wallet config is not a global startup gate.
- **The Wallet API is unreachable** (timeout, DNS failure, 502 from Wallet, etc.): the comparison view shows a "Wallet temporarily unavailable" banner with a Retry button; the PDF extraction result (which has already happened) is not discarded and is shown in a read-only mode so the user does not lose their place.
- **The user starts the import, then closes the tab**: rows already accepted by Wallet (HTTP success in the per-row response) remain in Wallet. The page makes no claim of cross-row atomicity; closing the tab does not undo accepted rows. Returning to the page and re-running the comparison shows the imported rows in the Wallet column.
- **A cardholder section in the PDF has no entry in the backend's label mapping**: rows from that section import with no label attached. The preview makes this visible per row before submit, so the user is not surprised. This is not an error.
- **A duplicate match by ±2 days has multiple competing Wallet candidates**: the row indicator names all candidates; the row defaults to unselected (because at least one candidate likely already covers it); the user explicitly opts in if they decide the PDF row is actually a separate transaction.
- **Re-uploading the same PDF in the same session**: the comparison view re-renders deterministically; selections and category picks made in the previous render do **not** persist across re-uploads (the user gets a fresh staging area each time a PDF is loaded).

## Requirements *(mandatory)*

### Functional Requirements

**Page placement & navigation**

- **FR-001**: The frontend MUST expose a new "Wallet Import" page reachable at its own URL (a new client-side route), distinct from the existing "Statement Extract" page. The existing "Statement Extract" page MUST keep working unchanged for users who only want to read a PDF without touching Wallet.
- **FR-002**: The Wallet Import page MUST be reachable via a visible navigation control (a link or tab) on every page where the existing "Statement Extract" page is reachable, so a first-time user can find it without typing a URL.

**Account selection (before upload)**

- **FR-003**: On entering the Wallet Import page, the backend MUST fetch the list of accounts from the Wallet API (`GET /v1/api/accounts`, excluding archived accounts) and the frontend MUST present them in a dropdown showing each account's display name and currency.
- **FR-004**: The user MUST pick exactly one Wallet account before uploading a PDF. The PDF upload control MUST be disabled until an account is selected, and the selection MUST be preserved across the upload step (the page does not silently reset it after the PDF is processed).

**PDF upload & extraction**

- **FR-005**: The Wallet Import page MUST accept exactly the same PDF inputs the existing extract flow accepts (single file, same size limits, same error envelope), and MUST invoke the existing backend extraction flow rather than a parallel one — there is one source of truth for "what's in this PDF".
- **FR-006**: All structured-error outcomes the existing extract flow can return (`INVALID_FILE_TYPE`, `FILE_TOO_LARGE`, `EMPTY_FILE`, `PASSWORD_PROTECTED`, `NO_TEXT_EXTRACTABLE`, `UNRECOGNIZED_LAYOUT`, `PARSE_FAILED`) MUST be surfaced on this page with the same user-visible behavior. A failed extraction does not advance the page to the comparison view.

**Loading existing Wallet records**

- **FR-007**: Once a PDF is extracted, the backend MUST fetch the existing records on the chosen Wallet account whose `recordDate` falls within the PDF's statement period extended by a fixed buffer of **±5 calendar days** at each boundary, walking through all paginated pages until exhausted, before the comparison view is shown to the user.
- **FR-008**: If Wallet's records endpoint returns a non-success response (e.g. 401, 403, 5xx, timeout), the comparison view MUST NOT be shown; instead, an error state MUST be shown that distinguishes "Wallet credentials invalid" (401/403), "Wallet temporarily unavailable" (5xx / network), and "Wallet rejected the query" (4xx other than 401/403). The PDF extraction result is preserved and shown read-only so the user does not have to re-upload.

**Duplicate detection rule**

- **FR-009**: For each transaction extracted from the PDF the backend MUST compute a duplicate match against the fetched Wallet records using the rule: **(a)** `recordDate` is within **±2 calendar days** of the PDF row's date, **(b)** the signed amount value is exactly equal (e.g. `-25.00` == `-25.00`), **(c)** the currency code matches, and **(d)** the direction matches (both expense, or both income — derived from sign). A PDF row may match zero, one, or many Wallet rows; the result is a per-PDF-row list of matched Wallet record ids.
- **FR-010**: A PDF row whose currency does not match the chosen Wallet account's currency MUST still appear in the comparison view, MUST be flagged with a currency-mismatch indicator, and MUST default to unselected for import. The dedup rule for currency-mismatched rows MUST NOT silently treat them as duplicates of differently-currencied Wallet rows.

**Comparison view**

- **FR-011**: The comparison view MUST display every extracted PDF row and every fetched Wallet row in a single page, with the pairings from FR-009 visually represented so the user can tell at a glance which PDF rows are likely already in Wallet and which Wallet rows are claimed by which PDF rows.
- **FR-012**: For every PDF row, the view MUST show: date, signed amount, currency, description (BAC concepto), the source cardholder section name from the PDF, and a checkbox for "import this row".
- **FR-013**: For every Wallet row, the view MUST show: date, signed amount, currency, note, counter-party, category name. Wallet rows MUST NOT be editable from this view; this feature only creates Wallet records, it does not modify them.
- **FR-014**: PDF rows that match at least one Wallet row by FR-009 MUST default to **unselected**; PDF rows with no Wallet match MUST default to **selected**. The user MAY toggle any row's selection.

**Category, payment type, labels for imported rows**

- **FR-015**: For every selected PDF row the user MUST pick a category from the list of Wallet categories (`GET /v1/api/categories`, fetched once on page load). Submit MUST be disabled until every selected row has a category.
- **FR-016**: For every imported row the `paymentType` field MUST be set to `credit_card`. The user does not pick paymentType in this iteration; future banks (e.g. checking-account statements) are out of scope.
- **FR-017**: For every imported row the backend MUST resolve `labelIds` from a configured mapping of **PDF cardholder section name → Wallet label id**, where the mapping lives in the backend's `appsettings` (or equivalent local configuration). A PDF row whose source cardholder section name has no entry in the mapping MUST be imported with no labels attached (not an error). The frontend's per-row preview MUST make the resolved label names (or "(no label)") visible before submit.

**Submit & per-row outcome**

- **FR-018**: When the user clicks the import action, the backend MUST send the selected PDF rows to Wallet via `POST /v1/api/records`, chunked into batches of at most **50** rows per request (Wallet's documented batch ceiling). The backend MUST process all batches before reporting back to the frontend.
- **FR-019**: For each submitted row the backend MUST surface a per-row outcome — success (with the new Wallet record id) or failure (with the row-level error message Wallet returned). A failure on one row MUST NOT prevent other rows from being created.
- **FR-020**: If Wallet responds with 401/403 to any batch, the backend MUST stop submitting subsequent batches (the credentials issue will affect every row) and MUST report a single "credentials invalid" outcome that the frontend renders as one banner, preserving the user's row selections so they can retry once an operator fixes the configured JWT.
- **FR-021**: After the import call completes, the frontend MUST offer a "Reload comparison from Wallet" action that re-fetches Wallet records for the same date window and re-renders the comparison view (so the user can visually confirm that imported rows now show on the Wallet side).

**Auth & API surface**

- **FR-022**: The Wallet API JWT (Bearer token) MUST live only in the backend's local configuration (e.g. `appsettings.json`, an environment variable, or another local secret store). The frontend MUST NOT have access to the JWT, MUST NOT send it on any request, and MUST NOT receive it in any response.
- **FR-023**: All Wallet API calls (`GET /v1/api/accounts`, `GET /v1/api/categories`, `GET /v1/api/records`, `POST /v1/api/records`) MUST go through the backend; the frontend MUST NOT call the Wallet API directly. The backend exposes its own endpoints to the frontend (the specific shapes are a planning concern, not a spec concern) and translates between the frontend's needs and the Wallet API.
- **FR-024**: At backend startup, if the Wallet JWT is not configured, the backend MUST start successfully and the existing "Statement Extract" flow MUST keep working; only Wallet-specific endpoints MUST fail with a clear "Wallet credentials not configured" response that the frontend's Wallet Import page renders as an operator-actionable message. Wallet config is not a global startup gate.

**Determinism, isolation, privacy**

- **FR-025**: For the same PDF, the same Wallet account, and the same fetched Wallet record set, the duplicate-detection result and the rendered comparison view MUST be byte-identical on re-render and across page refreshes. Sorting, pairing, and default selection MUST not depend on hash-set iteration order or other nondeterminism.
- **FR-026**: A row that succeeded into Wallet MUST appear in Wallet whether or not the user later closes the page; the feature makes no claim of all-or-nothing transactionality across the selection.
- **FR-027**: The backend MUST NOT log raw PDF bytes, full transaction descriptions, or the Wallet JWT at default log level. Per-row outcomes MUST be loggable as identifiers (PDF row index, Wallet record id on success, Wallet error code on failure) without the underlying description text. The logging-privacy constraint from `001-pdf-extract-web` carries forward.

**Out of scope for this iteration (do not implement)**

- Wallet record **updates** or **deletes**. This feature only **creates** Wallet records.
- Picking `paymentType` per row, or supporting non-credit-card statements (checking, savings, etc.). BAC PDFs are credit-card statements; `paymentType` is fixed to `credit_card`.
- Splitting one PDF across multiple Wallet accounts. One PDF → one account.
- Letting the user edit a fetched Wallet row from the comparison view.
- A re-importable "undo" of a successful Wallet POST. Rows that succeed are committed to Wallet; the user can delete them through Wallet's own UI.
- Persisting comparison state across sessions (no local storage, no server-side draft saves). Reopening the page is a fresh start.
- Hot-reload of the configured JWT or label mapping. Changes to those take effect on backend restart.
- Multi-user authentication, per-user JWTs, OAuth login flows, refresh-token handling. Single-user, operator-managed JWT only.
- A new bank besides BAC. The multi-bank seam from `002-multi-bank-support` already exists; adding a second bank is a separate spec, and the Wallet Import page reuses whatever bank's extraction the backend supports.
- Bulk category assignment ("apply this category to all selected"). Each row gets its category individually in this iteration.
- A dashboard, analytics, or historical "what did I import on which day" view of past imports.
- Internationalization of the new page (English UI strings are acceptable; matching the existing extract page's tone).
- Customizing the duplicate-detection window or label mapping from the UI. Both are fixed by the backend's configuration/code in this iteration.

### Key Entities

- **Wallet Account**: an account inside the user's Wallet budget tracker (id, display name, currency, account type, archived flag). Fetched from the Wallet API; presented to the user as a dropdown so they can pick which account the imported records will land in.
- **Wallet Category**: a category inside Wallet (id, display name). Fetched on page load; the user picks one per imported row. Required by Wallet on every created record.
- **Wallet Label**: a label inside Wallet (id, display name, color). Not user-picked; the backend resolves which labels to attach to each imported row by looking up the row's source cardholder section name in its configured mapping.
- **Wallet Record (existing)**: a transaction already in the user's Wallet account. Has a `recordDate`, a signed `amount` (with `currencyCode`), a category, optional labels, a `note`, and a `counterParty`. Read-only in this feature; used for duplicate detection and side-by-side display.
- **PDF Transaction (extracted)**: a transaction extracted by the existing extraction flow from an uploaded BAC PDF. Has a date, a signed amount with currency, a description (BAC's "concepto"), and a known source cardholder section name. Becomes a candidate Wallet record after the user picks a category and confirms.
- **Duplicate Match**: a relationship "this PDF row is likely already in Wallet" computed from the rule in FR-009 (±2 calendar days + same amount value + same currency + same direction). A PDF row may have zero, one, or many duplicate matches; each Wallet row may be claimed by zero, one, or many PDF rows.
- **Cardholder-Section → Label Mapping**: a backend-side configuration entry, one per cardholder section name the backend wants to label, with the Wallet label id to attach. Lives in `appsettings` (or local equivalent). Read once at backend startup; not edited at runtime.
- **Wallet Credentials**: a single JWT Bearer token configured on the backend (env var or `appsettings`) and used for every outbound Wallet API call. Never sent to the frontend; never logged.
- **Import Outcome**: the per-row result of a Wallet `POST /v1/api/records` call — success (with the new Wallet record id) or failure (with the row-level error message Wallet returned). Surfaced individually in the UI.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a BAC PDF whose statement period is known and a chosen Wallet account that has at least one record matching by the FR-009 rule, the comparison view correctly pairs **100%** of true-positive matches (verified against a hand-built fixture set of PDF rows ↔ Wallet rows) and reports **zero** false-positive pairings.
- **SC-002**: When the user clicks the import action with N selected rows that already have categories, **at least one Wallet record is created per selected row that the Wallet API accepts**, and every per-row outcome (success or failure) is reflected in the UI within **5 seconds** of completion for batches of up to **50 rows** total.
- **SC-003**: A partial-failure import (some rows succeed, some fail) does not roll back the successful rows. The user can verify this by clicking "Reload comparison from Wallet" — the successful rows appear in the Wallet column on the next render.
- **SC-004**: The existing "Statement Extract" page continues to work unchanged when the Wallet Import page is added — verified by re-running the existing frontend tests against the updated frontend and the existing backend integration tests against the updated backend; **100%** of existing tests pass without modification.
- **SC-005**: The Wallet JWT never appears in the rendered HTML, the JavaScript bundle, the browser's network panel for requests issued by the frontend, or any frontend-visible log. Verified by inspecting the deployed page and the network traffic on a real import.
- **SC-006**: For the same PDF + the same Wallet account + the same Wallet record snapshot, the comparison view renders the same pairings and the same default selections on every render, including across page refreshes — verified by an automated test that loads the page twice and diffs the rendered comparison state.
- **SC-007**: With no Wallet JWT configured, the backend still starts, the "Statement Extract" page still works end-to-end, and the Wallet Import page renders a clear operator-actionable "Wallet credentials not configured" message instead of a 500 or a blank state — verified by an automated test that boots the backend with the Wallet JWT config missing.
- **SC-008**: A user who has extracted a PDF can go from "the comparison view loaded" to "all chosen rows submitted and per-row outcomes shown" in under **2 minutes** for a typical statement (≤50 transactions, ≤25 selected), with the only mandatory per-row input being the category pick.

## Assumptions

- **The existing extraction flow is the only source of "what's in this PDF".** This feature does not re-implement PDF parsing; it consumes the existing extracted statement (sections, transactions, totals) from the existing backend extraction code path. If that path's output shape changes in a future spec, the Wallet Import page picks the change up for free.
- **One PDF → one Wallet account.** A BAC credit-card statement maps to a single Wallet account that the user picks up front. Splitting a single PDF across multiple Wallet accounts is explicitly out of scope.
- **The user knows which Wallet category each row belongs to.** No automatic categorization (LLM-driven or rule-driven) is in scope; categories are picked manually per row. A future spec may add automatic categorization, but this spec does not assume it.
- **Labels reflect cardholder sections.** The user wants to tag imported rows by which cardholder ("subcard") they came from in the BAC PDF. The mapping from cardholder section name → Wallet label id is configured by the operator in the backend's `appsettings` (or equivalent local config) and is read at backend startup. Cardholder sections without a configured mapping import with no labels — that is the user's chosen behavior, not an error.
- **`paymentType` is `credit_card` because BAC statements are credit-card statements.** Future banks/statements (checking, savings) would need to pick differently, but those banks are not in scope here.
- **Wallet API authentication is a long-lived Bearer JWT held by the backend.** The operator places the JWT into a backend config value (env var or `appsettings`) and refreshes it as needed; the feature does not implement OAuth, refresh tokens, or in-app credential entry.
- **Wallet's documented batch limits and pagination apply.** `POST /v1/api/records` accepts at most 50 records per call; the backend chunks larger selections. `GET /v1/api/records` is offset-paginated; the backend fetches all pages within the date window before showing the comparison view.
- **The PDF statement period is reliably extractable.** The existing extraction flow already returns a statement header with cutoff/issue dates that bound the comparison window; if it cannot, the user is prompted to confirm a date range manually (Edge Cases).
- **Currencies on Wallet accounts are stable for a given account during one import session.** The currency the Wallet account reports at page-load time is treated as authoritative for the whole session; mid-session currency changes are not handled.
- **The duplicate-detection rule from FR-009 is conservative on purpose.** It defaults likely-duplicates to *unselected* so the safest action (do nothing) is the default. The cost of a false-negative (asking the user to confirm a row that is really a duplicate) is much lower than the cost of a false-positive (silently re-creating a row the user already manually entered). The rule MAY be tuned in a future spec; this spec fixes it at ±2 days + exact amount + same currency + same direction.
- **The existing backend extraction error envelope is reused.** Wallet-specific failures (credentials invalid, Wallet unreachable, Wallet rejected the query) are surfaced through their own error states on the Wallet Import page; they do not pollute the existing `001-pdf-extract-web` error taxonomy. The frontend tells them apart by which page is showing them, not by overloading existing error codes.
- **The Wallet API contract documented under `docs/wallet/walletopenapi.json` is authoritative.** This spec assumes the endpoints `GET /v1/api/accounts`, `GET /v1/api/categories`, `GET /v1/api/records`, `POST /v1/api/records`, with the fields the OpenAPI doc names, behave as documented. Any drift from that contract is a separate problem and is out of scope here.
