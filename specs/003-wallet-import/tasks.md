---

description: "Task list for the Wallet Import from BAC PDF Statements feature"
---

# Tasks: Wallet Import from BAC PDF Statements

**Input**: Design documents from `/specs/003-wallet-import/`
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/openapi.yaml`, `quickstart.md` — all present.

**Tests**: Included. The spec defines per-user-story "Independent Test" criteria and SC-001/SC-004/SC-006/SC-007 are all verified through automated tests; tests are NOT optional for this feature.

**Organization**: Tasks are grouped by user story. The three user stories from `spec.md` are all P1 and build sequentially (US2 extends US1's view; US3 acts on US2's selection), but each is independently demoable at a checkpoint.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Different file from preceding tasks in the same phase AND no dependency on a still-incomplete task.
- **[Story]**: Maps task to user story (US1, US2, US3). Setup, Foundational, and Polish phases carry no story label.

## Path Conventions

This repo is a **web application**: backend in `src/CardStatement.Api/` + `src/CardStatement.Core/`, frontend in `frontend/`, tests in `tests/CardStatement.Tests/` + `tests/CardStatement.Api.Tests/` + `frontend/tests/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project-wide prerequisites that aren't story-specific.

- [ ] T001 Add `"react-router-dom": "^7"` to `frontend/package.json` dependencies; run `pnpm install` from `frontend/` to update `pnpm-lock.yaml`
- [ ] T002 [P] Append a `Wallet` section to `src/CardStatement.Api/appsettings.json` with `BaseUrl: ""`, `Jwt: ""`, `TimeoutSeconds: 30`, `LabelMapping: {}` (empty placeholder — operator fills in via Development settings)
- [ ] T003 [P] Create `src/CardStatement.Api/appsettings.Development.json` with a sample `Wallet` block (placeholder `BaseUrl`, `Jwt`, and `LabelMapping` entries for the BAC sample PDF's cardholder section names — see `quickstart.md` §1a). Confirm `appsettings.Development.json` is gitignored (extend `.gitignore` if not).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure used by ALL three user stories. Must complete before any user story phase begins.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Backend infrastructure (Wallet HTTP client + error mapping + endpoint skeleton)

- [ ] T004 [P] Create `src/CardStatement.Api/Wallet/WalletOptions.cs` per `data-model.md` §1 (`BaseUrl`, `Jwt`, `TimeoutSeconds=30`, `LabelMapping: Dictionary<string,string>`)
- [ ] T005 [P] Create `src/CardStatement.Api/Wallet/WalletApiException.cs` with `WalletApiErrorKind` enum (`NotConfigured`, `CredentialsInvalid`, `Unavailable`, `Rejected`) and exception class per `data-model.md` §2
- [ ] T006 [P] Create `src/CardStatement.Api/Wallet/IWalletApiClient.cs` interface with `ListAccountsAsync`, `ListCategoriesAsync`, `ListRecordsAsync(accountId, from, to)`, `CreateRecordsAsync(rows)` (signatures from `data-model.md` §2)
- [ ] T007 [P] Create `src/CardStatement.Api/Wallet/WalletEntities.cs` with internal records: `WalletAccount`, `WalletCategory`, `WalletRecord`, `WalletCreateRequest`, `WalletCreateOutcome` per `data-model.md` §2
- [ ] T008 [P] Create `src/CardStatement.Api/Wallet/Contracts/WalletErrorResponse.cs` (reuses existing `ErrorBody`) and a `WalletErrorCodes` static class with the 4 string constants (`WALLET_NOT_CONFIGURED`, `WALLET_CREDENTIALS_INVALID`, `WALLET_UNAVAILABLE`, `WALLET_REJECTED`)
- [ ] T009 Create `src/CardStatement.Api/Wallet/WalletApiClient.cs` implementing `IWalletApiClient` (depends on T004–T007): typed `HttpClient` constructor injection; `EnsureConfigured()` throws `WalletApiException(NotConfigured)` when `BaseUrl` or `Jwt` blank; per-request `Authorization: Bearer <jwt>` header from `IOptions<WalletOptions>`; `ListRecordsAsync` paginates with `limit=200` until `nextOffset` absent per `research.md` §6; error mapping per `research.md` §5 (401/403 → `CredentialsInvalid`, timeout/network/5xx → `Unavailable`, other 4xx → `Rejected`); logs at Warning include `BodyExcerpt` (≤256 chars) but never the JWT
- [ ] T010 Create `src/CardStatement.Api/Wallet/WalletErrorMapper.cs` static helper that converts `WalletApiException` → `IResult` with the correct HTTP status per `research.md` §5 table (`NotConfigured→503`, `CredentialsInvalid→502`, `Unavailable→504`, `Rejected→502`)
- [ ] T011 Create `src/CardStatement.Api/Wallet/Registration/WalletServiceCollectionExtensions.cs` with `AddWalletIntegration(IConfiguration)` that calls `services.Configure<WalletOptions>(config.GetSection("Wallet"))` and registers `IWalletApiClient`/`WalletApiClient` as a typed `HttpClient` (timeout from options)
- [ ] T012 Extract the existing PDF safety guards (size, empty, magic-bytes) out of `src/CardStatement.Api/Endpoints/ExtractEndpoint.cs` into a new `src/CardStatement.Api/Endpoints/PdfUploadGuard.cs` static helper; refactor `ExtractEndpoint.cs` to call the helper. Existing tests under `tests/CardStatement.Api.Tests/` MUST continue to pass unchanged.
- [ ] T013 Create `src/CardStatement.Api/Wallet/WalletImportEndpoint.cs` with `MapWalletImport(this IEndpointRouteBuilder)` extension method and stub handlers for `GET /api/wallet/accounts`, `GET /api/wallet/categories`, `POST /api/wallet/import/compare`, `POST /api/wallet/import/submit` — every stub returns 501 with a placeholder `WalletErrorResponse` for now
- [ ] T014 Edit `src/CardStatement.Api/Program.cs` to add `builder.Services.AddWalletIntegration(builder.Configuration);` after `AddBacBank()` and `app.MapWalletImport();` after `app.MapExtract();`. No behavior change to existing extract path; existing tests still pass.

### Frontend infrastructure (routing + Wallet types + Wallet client stub + Nav + error banner)

- [ ] T015 Edit `frontend/src/main.tsx` to wrap `<App />` in `<BrowserRouter>` from `react-router-dom`
- [ ] T016 Move the entire current content of `frontend/src/App.tsx` (the existing single-page extract UI) verbatim into a new file `frontend/src/pages/StatementExtractPage.tsx`, exporting `default function StatementExtractPage`. Update its imports for the new location.
- [ ] T017 Rewrite `frontend/src/App.tsx` to a router shell: imports `BrowserRouter`-related primitives, renders `<Nav />` + `<Routes>` mapping `/` → `<StatementExtractPage />` and `/wallet-import` → `<WalletImportPage />` (depends on T016 + T018 + T022)
- [ ] T018 [P] Create `frontend/src/components/Nav.tsx` rendering two `<NavLink>`s: "Statement Extract" → `/` and "Wallet Import" → `/wallet-import`, with active styling
- [ ] T019 [P] Create `frontend/src/types/wallet.ts` mirroring `contracts/openapi.yaml` schemas: `WalletAccount`, `WalletCategory`, `StatementWindow`, `PdfRow`, `WalletRow`, `CompareResponse`, `SubmitRequest`, `SubmitRequestRow`, `SubmitResponse`, `SubmitOutcome`, `WalletErrorCode` union type, `WalletErrorResponse`
- [ ] T020 [P] Create `frontend/src/api/walletClient.ts` with typed stub functions `listAccounts()`, `listCategories()`, `compare(file, accountId)`, `submit(payload)` — each returning `Result<T, WalletErrorResponse>` style; for now every function returns a rejected promise stub (real implementation in US1/US3 tasks)
- [ ] T021 [P] Create `frontend/src/components/WalletErrorBanner.tsx` that takes `{ code: WalletErrorCode; message: string }` and renders the 4 codes with operator-actionable copy (per `quickstart.md` §7 troubleshooting language)
- [ ] T022 [P] Create skeleton `frontend/src/pages/WalletImportPage.tsx` that just renders `<h1>Wallet Import</h1>` plus a "feature in progress" note — concrete state machine is built in US1
- [ ] T023 Append CSS to `frontend/src/styles.css` for `.app-nav`, `.nav-link`, `.nav-link--active`, and basic layout for the new page (existing CSS rules MUST remain unchanged)

### Foundational tests

- [ ] T024 [P] Create `tests/CardStatement.Api.Tests/Wallet/WalletNotConfiguredTests.cs` — boots `WebApplicationFactory` with `Wallet:Jwt` empty in config and asserts: (a) `POST /api/statements/extract` with the BAC sample PDF returns 200, (b) `GET /api/wallet/accounts`, `GET /api/wallet/categories`, `POST /api/wallet/import/compare` (with the BAC sample + dummy accountId), `POST /api/wallet/import/submit` (with a trivial body) all return 503 with `WALLET_NOT_CONFIGURED` envelope (verifies FR-024 + SC-007)
- [ ] T025 [P] Create `tests/CardStatement.Tests/Wallet/WalletApiClientPagingTests.cs` — fake `HttpMessageHandler` returns three pages of records with shrinking `nextOffset` then absent; asserts (a) accumulated result count + ordering, (b) the exact query strings (`recordDate=gte.<from>`, `recordDate=lt.<to+1d>`, `limit=200`, `offset=` per page), (c) `Authorization: Bearer <jwt>` header attached on every request
- [ ] T026 [P] Create `tests/CardStatement.Tests/Wallet/WalletApiClientErrorMappingTests.cs` — fake handler returns 401 → `WalletApiException(CredentialsInvalid)`, 500 → `Unavailable`, 400 → `Rejected`, simulated `HttpRequestException` (network) → `Unavailable`, `TaskCanceledException` (timeout) → `Unavailable`
- [ ] T027 [P] Create `frontend/tests/nav.test.tsx` — renders `<Nav>` inside a `MemoryRouter`, asserts both `<NavLink>`s present with correct `href` values, asserts active state on path change

**Checkpoint**: Foundation ready — user story implementation can now begin.

---

## Phase 3: User Story 1 — Side-by-side comparison view loads (Priority: P1) 🎯 MVP

**Goal**: A user picks a Wallet account, uploads a BAC PDF, and sees PDF rows + existing Wallet rows in the same period with duplicate pairings annotated.

**Independent Test**: Upload `samples/final5140_45178439_316493_0.pdf`, pick any Wallet account, observe the comparison view: (a) every PDF transaction appears in document order, (b) every Wallet record in `[issueDate-5d, cutoffDate+5d]` appears in `(date ASC, id ASC)` order, (c) PDF rows whose `(date±2d, signedAmount, currency, sign)` matches at least one Wallet record carry "likely duplicate" indicators, (d) Wallet rows show `claimedByPdfIndices`. No selection or import action needed.

### Backend US1 — matcher + label resolver + compare endpoint

- [ ] T028 [P] [US1] Create `src/CardStatement.Api/Wallet/LabelMappingResolver.cs` per `data-model.md` §4 — constructor takes `IOptions<WalletOptions>`, builds an internal case-insensitive `Dictionary<string,string>` once from the options snapshot, exposes `Resolve(rawName) → IReadOnlyList<string>` and `FindUnmapped(IEnumerable<string>) → IReadOnlyList<string>` (returns distinct sorted)
- [ ] T029 [P] [US1] Create `src/CardStatement.Api/Wallet/DuplicateMatcher.cs` with `internal sealed record PdfRowInternal(...)` (per `data-model.md` §3) and `internal static class DuplicateMatcher` exposing `Match(pdfRows, walletRecords) → IReadOnlyDictionary<int, IReadOnlyList<string>>` implementing FR-009 with the deterministic ordering contract from `research.md` §8
- [ ] T030 [P] [US1] Create the Compare wire DTOs in `src/CardStatement.Api/Wallet/Contracts/`: `WalletAccountDto.cs`, `WalletCategoryDto.cs`, `StatementWindowDto.cs`, `PdfRowDto.cs` (no `previewLabelIds/Names` yet — added in US2), `WalletRowDto.cs`, `CompareResponse.cs` — all per `data-model.md` §6
- [ ] T031 [US1] Create `src/CardStatement.Api/Wallet/WalletImportService.cs` per `data-model.md` §5 with `CompareAsync(string tempPdfPath, string accountId, CancellationToken)` implementing the 8-step flow from `data-model.md` §5 (PDF words → bank resolver → reconciler → window math from `research.md` §7 → parallel-fetch accounts+categories+records via `IWalletApiClient` → project PdfRowInternal in document order → `DuplicateMatcher.Match` → build `CompareResponse` with deterministic ordering). `SubmitAsync` MUST be present but throw `NotImplementedException` for now (filled in US3). Depends on T028, T029, T030.
- [ ] T032 [US1] Extend `AddWalletIntegration` in `src/CardStatement.Api/Wallet/Registration/WalletServiceCollectionExtensions.cs` to register `LabelMappingResolver` and `WalletImportService` (both as scoped or transient — pick scoped to match the request-scoped `IOptions` snapshot pattern)
- [ ] T033 [US1] Replace the `GET /api/wallet/accounts` stub in `src/CardStatement.Api/Wallet/WalletImportEndpoint.cs` with a real handler that calls `IWalletApiClient.ListAccountsAsync`, filters `Archived == true` out, maps to `WalletAccountDto`, returns `200 { accounts: [...] }`. `WalletApiException` is caught and routed through `WalletErrorMapper`.
- [ ] T034 [US1] Replace the `GET /api/wallet/categories` stub in `WalletImportEndpoint.cs` with a real handler that calls `IWalletApiClient.ListCategoriesAsync`, maps to `WalletCategoryDto`, returns `200 { categories: [...] }`. Same error routing.
- [ ] T035 [US1] Replace the `POST /api/wallet/import/compare` stub in `WalletImportEndpoint.cs` with a real handler that: validates multipart `file` + `accountId` form fields, runs `PdfUploadGuard.Check(file, config)` (returns the existing `ExtractionErrorResponse` envelope on guard failure), copies the upload to a `TempPdfFile`, calls `WalletImportService.CompareAsync(tempPath, accountId, ct)`, returns `200 CompareResponse`. Existing extraction exceptions are mapped via the existing `ExtractionFailureMapper`; `WalletApiException` is mapped via `WalletErrorMapper`.

### Backend US1 tests

- [ ] T036 [P] [US1] Create `tests/CardStatement.Tests/Wallet/DuplicateMatcherTests.cs` — table-driven xUnit `[Theory]` covering FR-009: dates inside window (0d, 1d, 2d) and outside (3d), equal/unequal amounts, sign mismatch (expense vs income with same magnitude), currency mismatch, many-to-one (2 pdf rows ↔ 1 wallet record), one-to-many (1 pdf row ↔ 3 wallet records). Plus a determinism test asserting `Match(a,b)` returns byte-equal `Dictionary` on two consecutive calls (verifies FR-025 / SC-006).
- [ ] T037 [P] [US1] Create `tests/CardStatement.Tests/Wallet/LabelMappingResolverTests.cs` — case-insensitive lookup hit/miss, accented names, `FindUnmapped` is distinct + sorted, empty mapping returns empty results.
- [ ] T038 [P] [US1] Create `tests/CardStatement.Api.Tests/Wallet/WalletAccountsAndCategoriesEndpointTests.cs` — `WebApplicationFactory` with a fake `HttpMessageHandler` returning canned `/v1/api/accounts` and `/v1/api/categories` payloads; asserts: (a) happy-path responses, (b) archived accounts filtered out, (c) 401 from upstream → 502 `WALLET_CREDENTIALS_INVALID`, (d) network error → 504 `WALLET_UNAVAILABLE`.
- [ ] T039 [US1] Create `tests/CardStatement.Api.Tests/Wallet/WalletCompareEndpointTests.cs` — uses the BAC sample PDF `samples/final5140_45178439_316493_0.pdf` + a hand-built Wallet records snapshot via fake `HttpMessageHandler`; asserts: (a) `CompareResponse.pdfRows` matches expected rows in document order, (b) the expected pairings are produced (one hand-picked PDF row matches one hand-built Wallet record by ±2d + amount + currency rule), (c) `defaultSelected` is `false` on paired rows and `true` on unpaired rows, (d) `unmappedSections` lists cardholder names not present in the test's configured `LabelMapping`, (e) re-running the same test twice gives byte-identical `CompareResponse` JSON (FR-025).

### Frontend US1 — picker + comparison view + API client wiring

- [ ] T040 [P] [US1] Create `frontend/src/components/WalletAccountPicker.tsx` — `<select>` of `WalletAccount[]` props, each option labelled `"<name> (<currencyCode>)"`, calls `onChange(accountId)`
- [ ] T041 [P] [US1] Create `frontend/src/components/ComparisonTable.tsx` — side-by-side two-column layout: left "PDF rows" using `PdfRow[]`, right "Wallet rows" using `WalletRow[]`. Each PDF row renders date, signed amount, currency, description, source cardholder + cardLast4. Each Wallet row renders date, signed amount, currency, note, counter-party, category name. Pairings drawn via `matchedWalletRecordIds` and `claimedByPdfIndices` — a small badge "🔗 Matches W-…" on PDF rows; "↩ Claimed by row #…" on Wallet rows. (Selection checkboxes + category dropdowns wait for US2 — render the checkbox as read-only mirroring `defaultSelected` for now.)
- [ ] T042 [US1] Replace the stub `walletClient.ts` functions `listAccounts()`, `listCategories()`, `compare(file, accountId)` with real `fetch`-based implementations returning typed `Result<…, WalletErrorResponse | ExtractionErrorResponse>` discriminated unions matching `statementsClient.ts`'s style. Compare uses `FormData`.
- [ ] T043 [US1] Implement the `WalletImportPage` state machine in `frontend/src/pages/WalletImportPage.tsx` per `data-model.md` §8: `useReducer` with states `loadingAccounts → accountsReady → uploading → comparing → comparisonReady → walletError`. On mount, fetches accounts; renders `WalletAccountPicker` + (when account picked) the existing `UploadForm` (reused from US1's components); on upload, calls `walletClient.compare`; renders `ComparisonTable` on success or `WalletErrorBanner` on Wallet errors / existing `ErrorBanner` on extraction errors. Submit control is disabled with a "(comes in US2)" tooltip for now.

### Frontend US1 tests

- [ ] T044 [P] [US1] Create `frontend/tests/walletAccountPicker.test.tsx` — renders all options including currencyCode, calls onChange with the id when an option is picked
- [ ] T045 [P] [US1] Create `frontend/tests/comparisonTable.us1.test.tsx` — renders provided PDF rows in input order and Wallet rows in input order, draws "Matches W-…" badge on rows whose `matchedWalletRecordIds.length > 0`, draws "Claimed by row #…" on rows whose `claimedByPdfIndices.length > 0`
- [ ] T046 [US1] Create `frontend/tests/walletImportPage.us1.test.tsx` — stubs `fetch` for `/api/wallet/accounts` and `/api/wallet/import/compare`; asserts the flow: page renders → accounts dropdown populates → pick account → upload control unlocks → upload file → comparison view appears with expected paired row; tests 401 error path → `WalletErrorBanner` shows the operator-actionable message

**Checkpoint US1**: User Story 1 is fully functional and demoable independently — a user can extract a BAC PDF and see it side-by-side with Wallet records, with duplicates highlighted. No interaction beyond the comparison view yet.

---

## Phase 4: User Story 2 — Select rows + per-row category + preview (Priority: P1)

**Goal**: From the comparison view, the user selects which rows to import, picks a category per selected row, and sees a per-row preview of the Wallet payload that will be submitted. Submit is disabled until every selected row has a category.

**Independent Test**: With a loaded comparison view, toggle row selection, pick categories, inspect the per-row preview. Verify: (a) duplicate-paired rows default unselected; (b) others default selected; (c) toggling persists in the page; (d) submit is disabled when any selected row has no category; (e) preview shows literal Wallet payload fields (account, date, signed amount, currency, paymentType=`credit_card`, category name, label names from the cardholder-section mapping or "(no label)", note, counterParty); (f) rows from cardholder sections in `unmappedSections` show "(no label)" in the preview.

### Backend US2 — labels endpoint + per-row label preview

- [ ] T047 [P] [US2] Extend `IWalletApiClient` with `ListLabelsAsync(CancellationToken) → IReadOnlyList<WalletLabel>`; add `WalletLabel(string Id, string Name, string? Color, bool Archived)` to `WalletEntities.cs`; implement `ListLabelsAsync` in `WalletApiClient` with the same pagination + auth pattern as `ListRecordsAsync` (against `/v1/api/labels?limit=200&offset=…`)
- [ ] T048 [US2] Extend `PdfRowDto` in `src/CardStatement.Api/Wallet/Contracts/PdfRowDto.cs` with two new optional-but-always-emitted fields `previewLabelIds: IReadOnlyList<string>` and `previewLabelNames: IReadOnlyList<string>`. Update `data-model.md` §6 `PdfRowDto` definition and `contracts/openapi.yaml` `PdfRow` schema to include both arrays (additive, both `required`).
- [ ] T049 [US2] Update `WalletImportService.CompareAsync` to: (a) fetch labels in the same `Task.WhenAll` batch with accounts/categories/records, (b) build an `id → name` lookup, (c) for each `PdfRowInternal`, call `LabelMappingResolver.Resolve(cardholderSectionRawName)` and project both `previewLabelIds` and `previewLabelNames` onto the emitted `PdfRowDto` (empty arrays when unmapped). `unmappedSections` remains the distinct-cardholders summary.

### Backend US2 tests

- [ ] T050 [P] [US2] Create `tests/CardStatement.Tests/Wallet/WalletLabelsPagingTests.cs` — fake handler, two-page response, asserts accumulation and query strings
- [ ] T051 [US2] Extend `WalletCompareEndpointTests.cs` (from T039) with new cases asserting `previewLabelIds`/`previewLabelNames` are correctly populated for rows whose cardholder section is configured in `LabelMapping`, and remain empty for rows whose cardholder is in `unmappedSections`

### Frontend US2 — dropdown + preview + selection state

- [ ] T052 [P] [US2] Create `frontend/src/components/CategoryDropdown.tsx` — reusable `<select>` of `WalletCategory[]`, props `value: string | null`, `onChange(categoryId)`, `placeholder="-- pick a category --"`, fires onChange with `categoryId` (not the whole object)
- [ ] T053 [P] [US2] Create `frontend/src/components/PerRowPreview.tsx` — given a `PdfRow`, `accountName`, and the selected `categoryName`, renders a compact panel showing every Wallet payload field literally: `accountId → accountName`, `recordDate` (ISO), `amount.value` (signed), `amount.currencyCode`, `paymentType: credit_card`, `categoryId → categoryName`, `labelIds → previewLabelNames.join(", ") || "(no label)"`, `note: description`, `counterParty`
- [ ] T054 [US2] Update `frontend/src/types/wallet.ts` `PdfRow` type with `previewLabelIds: string[]` and `previewLabelNames: string[]`
- [ ] T055 [US2] Extend `WalletImportPage` reducer with `selections: Record<number, boolean>` (initialized from each `PdfRow.defaultSelected`) and `categoryByIndex: Record<number, string | null>` (initialized to `null`). Extend `ComparisonTable` to (a) render a real selection checkbox per PDF row wired to `selections`, (b) render `CategoryDropdown` when `selections[index]` is true, (c) show "needs category" warning per selected row without a category, (d) show "Selected: N" counter at the top of the PDF column, (e) compute `canSubmit = every selected row has a category && N > 0` and disable the Submit button accordingly, (f) embed `PerRowPreview` as an expandable section per selected row

### Frontend US2 tests

- [ ] T056 [P] [US2] Create `frontend/tests/categoryDropdown.test.tsx` — renders the categories list, fires onChange with the id when an option is picked, renders placeholder when value is null
- [ ] T057 [P] [US2] Create `frontend/tests/perRowPreview.test.tsx` — asserts every payload field present, that `previewLabelNames.length === 0` renders "(no label)", that `signedAmount` is shown with its sign, that `paymentType` is the literal `credit_card`
- [ ] T058 [US2] Create `frontend/tests/walletImportPage.us2.test.tsx` — from the comparison view: (a) duplicates start unselected, others selected; (b) toggling persists; (c) submit disabled when at least one selected row lacks a category; (d) submit enables only when every selected row has a category; (e) preview reflects the chosen category name and the auto-resolved labels (or "(no label)")

**Checkpoint US2**: User Story 2 is fully functional — the user can stage exactly what they want to import, with full visibility into what will be sent.

---

## Phase 5: User Story 3 — Submit and see per-row outcome (Priority: P1)

**Goal**: The user clicks Import to Wallet, the backend POSTs to Wallet in chunks of ≤50, and the UI shows each row's outcome (✅ + new id, or ❌ + error). The user can then reload the comparison to verify imported rows now appear on the Wallet side.

**Independent Test**: With a staged selection (N ≤ 100 rows with categories assigned), click Import to Wallet. Assert: (a) backend issues exactly `ceil(N/50)` POSTs to the upstream Wallet API in input order, (b) each row's outcome surfaces individually within seconds, (c) on a mixed-outcome batch the successful rows show ✅ and failed rows show their error message + remain selectable for retry, (d) on a 401 from Wallet during submit, the page shows a single "credentials invalid" banner and preserves selection + category state, (e) Reload comparison from Wallet re-fetches and successful rows appear on the Wallet side.

### Backend US3 — submit DTOs + SubmitAsync + endpoint

- [ ] T059 [P] [US3] Create the Submit DTOs in `src/CardStatement.Api/Wallet/Contracts/`: `SubmitRequest.cs`, `SubmitRequestRow.cs`, `SubmitResponse.cs`, `SubmitOutcomeDto.cs` — all per `data-model.md` §6
- [ ] T060 [US3] Implement `WalletImportService.SubmitAsync(SubmitRequest req, CancellationToken)` per `data-model.md` §5: (a) for each `SubmitRequestRow`, build a `WalletCreateRequest` with `paymentType = "credit_card"`, `accountId = req.AccountId`, `labelIds = LabelMappingResolver.Resolve(row.CardholderSectionRawName)`; (b) `Chunk(50)` preserving order; (c) for each chunk call `await walletClient.CreateRecordsAsync(chunk, ct)`; (d) translate each `WalletCreateOutcome` into a `SubmitOutcomeDto` preserving the original input `Index`; (e) on `WalletApiException(CredentialsInvalid)` from any chunk, stop iterating and rethrow (the endpoint maps this to the top-level banner per FR-020). Other `WalletApiException` kinds also propagate.
- [ ] T061 [US3] Replace the `POST /api/wallet/import/submit` stub in `src/CardStatement.Api/Wallet/WalletImportEndpoint.cs` with a real handler that (a) validates `accountId` non-empty, `rows` non-empty, and every `row.categoryId` non-empty — returning HTTP 400 with `WalletErrorResponse(WALLET_REJECTED, "Missing categoryId for row indices [n, m, ...]")` if any fail; (b) calls `WalletImportService.SubmitAsync`; (c) returns `200 SubmitResponse` on success; (d) maps `WalletApiException` via `WalletErrorMapper`

### Backend US3 tests

- [ ] T062 [P] [US3] Create `tests/CardStatement.Api.Tests/Wallet/WalletSubmitEndpointTests.cs` — fake `HttpMessageHandler` cases: (a) happy single-batch (10 rows → 1 POST → 10 outcomes), (b) chunking (60 rows → 2 POSTs of 50+10 in order → 60 outcomes), (c) partial 207 batch (mix of success and per-row error; assert outcomes preserve indices), (d) 401 from upstream mid-batch (first chunk OK, second chunk returns 401 → endpoint returns 502 `WALLET_CREDENTIALS_INVALID`; the first chunk's successes are NOT reported back, because the endpoint surfaces a top-level banner per FR-020 — verify the upstream still saw the first chunk's POST so the rows are committed), (e) `WalletApiException(Unavailable)` from upstream → 504, (f) validation: missing categoryId on a row → 400 with the row indices listed
- [ ] T063 [US3] Extend `WalletSubmitEndpointTests` with cases for missing `accountId` and empty `rows` array (both → 400 `WALLET_REJECTED`)

### Frontend US3 — outcome list + submit wiring + reload action

- [ ] T064 [P] [US3] Create `frontend/src/components/SubmitOutcomeList.tsx` — given `SubmitOutcome[]`, renders each row with ✅ + `walletRecordId` (linked text — display only, no actual link) on success, ❌ + `errorMessage` on failure; provides a "Retry failed rows" affordance for failed rows
- [ ] T065 [US3] Implement `walletClient.submit(SubmitRequest)` in `frontend/src/api/walletClient.ts` matching the existing `compare()` pattern (Result discriminated union, JSON body)
- [ ] T066 [US3] Extend `WalletImportPage` reducer with `submitting` and `outcomeReady` states. When user clicks Import to Wallet: build `SubmitRequest` from the current `compare.pdfRows` filtered by `selections[index]`, with `categoryId = categoryByIndex[index]`; dispatch `submitting`; call `walletClient.submit`; on success dispatch `outcomeReady` with `outcomes`; render `<SubmitOutcomeList />` below the comparison view in that state
- [ ] T067 [US3] Add a "Reload comparison from Wallet" button visible in the `outcomeReady` state. When clicked: keep the originally uploaded `File` from state (it was preserved when entering `uploading`), dispatch `comparing`, and re-call `walletClient.compare(file, accountId)`. On success, transition to `comparisonReady` with a **fresh** `selections` (defaults from the new pairings) and **fresh** `categoryByIndex` (all `null`). Spec edge case: "selections and category picks made in the previous render do not persist across re-uploads."
- [ ] T068 [US3] On `WalletApiException(CredentialsInvalid)` from submit (the endpoint returns 502 `WALLET_CREDENTIALS_INVALID`), the reducer MUST transition to `walletError` with `previousState: 'comparisonReady'` so that the user's `selections` + `categoryByIndex` are preserved; the WalletErrorBanner shows the operator-actionable message; clicking the banner's "Try again" button returns to `comparisonReady` with the staged data intact

### Frontend US3 tests

- [ ] T069 [P] [US3] Create `frontend/tests/submitOutcomeList.test.tsx` — renders ✅ + id on success, ❌ + message on failure; "Retry failed rows" button calls the provided handler with the failed indices
- [ ] T070 [US3] Create `frontend/tests/walletImportPage.us3.test.tsx` — stubs `/api/wallet/import/submit`; asserts: (a) happy path (3 selected rows → SubmitOutcomeList shows 3 successes), (b) partial failure (1 success + 1 failure → both surfaced; failed row remains selectable), (c) 401 mid-submit → `WalletErrorBanner` shows credentials-invalid copy AND selection + category state still present when banner is dismissed, (d) Reload comparison re-calls `compare()` and renders fresh pairings with fresh selections

**Checkpoint US3**: User Story 3 is fully functional — the feature is end-to-end usable. SC-002, SC-003, SC-008 are now verifiable on the running app.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final hardening, regression gate, and operator-facing touches.

- [ ] T071 [P] Create `tests/CardStatement.Api.Tests/Wallet/WalletImportEndToEndTests.cs` — single test: BAC sample PDF + fake `HttpMessageHandler` returning a multi-page Wallet records snapshot + categories + labels + a configured `LabelMapping`; runs Compare → asserts comparison view; runs Submit with a subset of rows → asserts 2-chunk POST sequence + per-row outcomes; runs Compare again → asserts the previously-submitted rows now appear in the Wallet column (paired with the corresponding PDF rows). This is the SC-001 + SC-002 + SC-003 acceptance test.
- [ ] T072 [P] Audit logging in `WalletApiClient` and `WalletImportService` for FR-027: ensure no `LogInformation`/`LogWarning`/`LogError` statement emits `options.Jwt`, `PdfRow.description`, `WalletRecord.note`, or full `SubmitRequestRow.description`. Add a `LoggingPrivacyAuditTests.cs` smoke test in `tests/CardStatement.Api.Tests/Wallet/` that uses an in-memory log capture, runs a Compare + Submit happy path, and asserts no JWT substring + no description substring appears in any captured log line.
- [ ] T073 [P] Final pass on `frontend/src/styles.css`: comparison view column layout, pairing highlight color, selected/duplicate/needs-category state badges, submit-disabled cursor. Existing CSS rules from `001`/`002` MUST remain unchanged.
- [ ] T074 Run `dotnet test` and `cd frontend && pnpm test` end-to-end. All existing tests from `001`/`002` MUST still pass (SC-004 regression gate). If a pre-existing test fails, fix it before merging — no skips.
- [ ] T075 Walk `quickstart.md` end-to-end against a real Wallet account (or document deviations encountered): configure JWT, start backend, start frontend, navigate to `/wallet-import`, pick account, upload BAC sample PDF, see comparison view, submit a single row, see outcome, reload comparison, see the imported row on the Wallet side.
- [ ] T076 [P] Ensure `.gitignore` excludes `src/CardStatement.Api/appsettings.Development.json` (or document why it shouldn't be) so a developer's JWT never gets committed.
- [ ] T077 [P] Add a brief "Wallet Import" section to top-level `README.md` pointing readers at `specs/003-wallet-import/quickstart.md` for setup + walkthrough.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately. T001 / T002 / T003 are independent.
- **Foundational (Phase 2)**: Depends on Phase 1 (specifically: T002/T003 must run before T004; T001 must run before T015). Within Phase 2, the backend infra block (T004–T014) and frontend infra block (T015–T023) are themselves independent of each other and can be split between two developers. Foundational tests (T024–T027) can be written in parallel with the corresponding infra tasks they validate.
- **User Stories (Phases 3–5)**: All depend on Phase 2 completion. US1 must complete (at least its backend) before US2 starts (US2 extends the Compare endpoint + the comparison view). US3 needs only US2's frontend selection state and US1's backend; backend US3 can be done in parallel with frontend US2 if staffing allows.
- **Polish (Phase 6)**: Depends on all three user stories complete.

### User Story Dependencies

- **US1**: Independent (after Foundational). MVP slice — viable end-to-end demo after this phase.
- **US2**: Extends US1's `PdfRowDto` (adds preview label fields) and `ComparisonTable` (adds checkbox + dropdown + preview). Cannot start its **frontend** until US1's frontend lands. **Backend US2** (T047–T051) can start as soon as US1's backend service exists.
- **US3**: Extends US2's selection state (US2 → frontend US3) and US1's Wallet client (US1 → backend US3). Backend US3 (T059–T063) can run in parallel with frontend US2 (T052–T058).

### Within Each User Story

- Backend types/records (entity + DTOs) before backend service/orchestrator before backend endpoint.
- Backend endpoint complete before frontend page can integrate.
- Frontend types/components before page wiring.
- Tests written alongside the production code they verify (each task that introduces production code lists its corresponding test task next to it).

### Parallel Opportunities

- **Phase 1**: T002 and T003 are different files; T001 can also run alongside.
- **Phase 2 backend**: T004, T005, T006, T007, T008 are all different files with no inter-deps → 5-way parallel. T009/T010/T011 each depend on the earlier T004–T008 batch but are then sequential.
- **Phase 2 frontend**: T018, T019, T020, T021, T022 are all different files → 5-way parallel after T015 (router setup). T016 and T017 are sequential.
- **Phase 2 tests**: T024, T025, T026, T027 are entirely parallel.
- **US1**: T028, T029, T030, T036, T037, T038 are different files → 6-way parallel. T040, T041, T044, T045 are 4-way parallel on the frontend side.
- **US2 / US3**: Backend US3 (T059, T062) can run in parallel with frontend US2 (T052–T058) if you have two developers.

---

## Parallel Example: User Story 1

```bash
# Wave 1 — pure types and pure algorithms (any 3 of these can run on different machines):
Task: "T028 Create LabelMappingResolver in src/CardStatement.Api/Wallet/LabelMappingResolver.cs"
Task: "T029 Create DuplicateMatcher + PdfRowInternal in src/CardStatement.Api/Wallet/DuplicateMatcher.cs"
Task: "T030 Create Compare DTOs in src/CardStatement.Api/Wallet/Contracts/"

# Wave 2 — tests against the matcher and resolver (in parallel with each other and with Wave 3 frontend):
Task: "T036 DuplicateMatcherTests.cs"
Task: "T037 LabelMappingResolverTests.cs"

# Wave 3 — frontend components (no dep on backend artifacts):
Task: "T040 WalletAccountPicker.tsx"
Task: "T041 ComparisonTable.tsx (US1 read-only variant)"

# Wave 4 — wiring (sequential because each one depends on the previous):
T031 (WalletImportService) → T032 (DI) → T033/T034/T035 (endpoints) → T039 (compare endpoint test) → T042 (walletClient) → T043 (page) → T046 (page test)
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1: Setup (~3 small tasks).
2. Complete Phase 2: Foundational (24 tasks). At the end, the page loads with a `loadingAccounts` spinner that resolves once the operator has configured `Wallet:Jwt`.
3. Complete Phase 3: User Story 1 (19 tasks).
4. **STOP and VALIDATE**: end-to-end demo of "see PDF rows next to Wallet rows with pairings". No submit yet.
5. Deploy/demo if ready.

### Incremental Delivery

- Phase 1 + 2 → Foundation. Wallet auth wired, error envelope shipped, both pages exist (Wallet Import page shows the comparison-view placeholder).
- Phase 3 (US1) → MVP. Comparison view loads; can demonstrate the value to anyone who needs the dedup diagnostic.
- Phase 4 (US2) → Staged selection. Now there's an actionable workflow even if no rows are submitted.
- Phase 5 (US3) → Full feature. Live import + per-row outcome + reload. Spec is fully delivered.
- Phase 6 → Hardening + docs. Mergeable as a single PR before US3 lands, or as a separate hardening PR after.

### Parallel Team Strategy

With two developers:

1. Both work Phase 1 + 2. Dev A on backend (T004–T014, T024–T026), Dev B on frontend (T015–T023, T027).
2. After Phase 2, Dev A continues into backend US1 (T028–T031, T036–T039). Dev B starts frontend US1 (T040–T046).
3. Dev A picks up backend US2 (T047–T051) and backend US3 (T059–T063) while Dev B continues into frontend US2 (T052–T058).
4. Both converge on frontend US3 (T064–T070) and Polish.

---

## Validation Summary

- **Total tasks**: 77 (3 setup + 24 foundational + 19 US1 + 12 US2 + 12 US3 + 7 polish).
- **Format**: every task uses the `- [ ] TNNN [P?] [USx?] description with file path` checklist format.
- **MVP scope**: Phases 1–3 (46 tasks). Deliverable, demoable, and valuable on its own (the diagnostic side-by-side view).
- **Independent test criteria**: each user story phase carries its own "Independent Test" paragraph linking back to the spec's per-story success conditions (FR-009/FR-011/FR-014 for US1; FR-015/FR-017 for US2; FR-018–FR-021 for US3).
- **Parallel opportunities**: 5-way parallel on Phase 2 backend types; 5-way parallel on Phase 2 frontend infra; 6-way parallel on US1 backend types+tests+frontend components; 2-developer split across US2/US3.

---

## Notes

- `[P]` tasks share no file with other in-flight tasks in the same wave and depend only on already-completed tasks.
- All test tasks are paired with the production tasks they verify; the spec calls for verifiable success criteria, so tests are NOT optional in this feature.
- Commit after each task or at the end of each wave (project hooks already offer optional `/speckit-git-commit` between phases).
- Stop at any Checkpoint to validate the user story independently before moving on.
- Avoid cross-story file conflicts: US1, US2, US3 each touch the same `WalletImportPage.tsx` reducer and `ComparisonTable.tsx` — tasks list the additive edits each story makes, but the engineer doing US2/US3 MUST re-read the file before editing, since US1's changes are in place.
