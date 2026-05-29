---
description: "Task list for spec 001-pdf-extract-web: PDF Extract & Display (Web MVP)"
---

# Tasks: PDF Extract & Display (Web MVP)

**Input**: Design documents from `/specs/001-pdf-extract-web/`
**Prerequisites**: `plan.md` (required), `spec.md` (required for user stories), `research.md`, `data-model.md`, `contracts/openapi.yaml`, `quickstart.md`

**Tests**: Included. The `research.md` §R10 commits to a concrete test stack on both sides, and **SC-002** (row-for-row parity with the existing console PoC's `result.json`) is only verifiable via the backend parity test. Tests are listed alongside the implementation of the user story they verify (not strict TDD ordering — write impl + tests together, run tests last).

**Organization**: Tasks are grouped by user story so each story is independently completable and demoable. **MVP = User Story 1 only.**

## Format: `[ID] [P?] [Story] Description with file path`

- **[P]**: parallelizable (different files, no dependencies on incomplete tasks).
- **[Story]**: which user story the task belongs to (`[US1]`, `[US2]`, `[US3]`). Setup, Foundational, and Polish tasks have **no** story label.
- File paths are absolute-from-repo-root.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project scaffolds (backend, backend tests, frontend) and base configuration files. No business logic.

- [x] T001 Create `src/CardStatement.Api/CardStatement.Api.csproj` with `Sdk="Microsoft.NET.Sdk.Web"`, framework `net10.0` (inherited via `Directory.Build.props`), and `<ProjectReference Include="../CardStatement.Core/CardStatement.Core.csproj" />`.
- [x] T002 Create empty `src/CardStatement.Api/Program.cs` with `var builder = WebApplication.CreateBuilder(args); var app = builder.Build(); app.Run();` so the project compiles.
- [x] T003 Register `src/CardStatement.Api/CardStatement.Api.csproj` in `CreditStatementParser.slnx` under the `/src/` folder.
- [x] T004 Create `tests/CardStatement.Api.Tests/CardStatement.Api.Tests.csproj` as an xUnit test project with `<ProjectReference Include="../../src/CardStatement.Api/CardStatement.Api.csproj" />` and `<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="*" />`.
- [x] T005 Register `tests/CardStatement.Api.Tests/CardStatement.Api.Tests.csproj` in `CreditStatementParser.slnx` under the `/tests/` folder.
- [x] T006 [P] Scaffold the frontend by running `pnpm create vite@latest frontend -- --template react-ts` from the repo root (writes `frontend/package.json`, `frontend/vite.config.ts`, `frontend/index.html`, `frontend/src/main.tsx`, `frontend/src/App.tsx`, `frontend/tsconfig.json`).
- [x] T007 [P] In `frontend/`, run `pnpm add -D vitest @testing-library/react @testing-library/jest-dom @testing-library/user-event jsdom` to add frontend test dependencies.
- [x] T008 [P] Configure Vitest in `frontend/vite.config.ts` (`test: { environment: 'jsdom', globals: true, setupFiles: ['./tests/setup.ts'] }`) and create `frontend/tests/setup.ts` importing `@testing-library/jest-dom`.
- [x] T009 [P] Create `frontend/.env.example` containing `VITE_API_BASE_URL=http://localhost:5080` and document copying it to `.env.local` in `quickstart.md` (already documented; verify).
- [x] T010 [P] Create `src/CardStatement.Api/appsettings.json` with `Kestrel:Limits:MaxRequestBodySize=26214400`, `Cors:AllowedOrigins=["http://localhost:5173"]`, `Upload:MaxBytes=26214400`, and a sensible `Logging:LogLevel:Default=Information`.
- [x] T011 [P] Add `frontend/node_modules/`, `frontend/dist/`, `frontend/.env.local`, `frontend/coverage/` to the root `.gitignore`.

**Checkpoint**: `dotnet build CreditStatementParser.slnx` succeeds. `cd frontend && pnpm dev` boots Vite at `http://localhost:5173`. No business logic yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: DTO contract scaffolding, dependency injection, JSON/CORS/Kestrel config, frontend types and state-machine shell. After this phase, every user story can layer onto the same backbone without touching shared code.

**⚠️ CRITICAL**: No user-story work begins until this phase is complete.

### Backend DI & infrastructure (sequential — all touch `Program.cs`)

- [x] T012 In `src/CardStatement.Api/Program.cs`, register `CardStatement.Core` services with DI: `services.AddSingleton<IPdfExtractor, PdfExtractor>()`, `services.AddSingleton<IStatementParser, StatementParser>()`, `services.AddSingleton<IReconciler, Reconciler>()` (use the concrete implementations already present in `src/CardStatement.Core`).
- [x] T013 In `src/CardStatement.Api/Program.cs`, configure `System.Text.Json` via `services.ConfigureHttpJsonOptions(o => { o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase; o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)); })`.
- [x] T014 In `src/CardStatement.Api/Program.cs`, bind `Kestrel:Limits:MaxRequestBodySize` from configuration via `builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = builder.Configuration.GetValue<long>("Kestrel:Limits:MaxRequestBodySize"));`.
- [x] T015 In `src/CardStatement.Api/Program.cs`, register CORS with a named policy `"frontend"` reading from `Cors:AllowedOrigins`, and apply it with `app.UseCors("frontend")`.
- [x] T016 In `src/CardStatement.Api/Program.cs`, configure logging via `builder.Logging.ClearProviders().AddSimpleConsole()` at `Information` level, and document in a code comment that PDF bytes and full transaction descriptions MUST NOT be logged at default level (R9 in `research.md`).

### Backend DTO contracts (parallelizable — each in its own file)

- [x] T017 [P] Create `src/CardStatement.Api/Contracts/StatementPeriodDto.cs` as a `public sealed record StatementPeriodDto(DateOnly IssueDate, DateOnly CutoffDate)` matching `openapi.yaml#/components/schemas/StatementPeriod`.
- [x] T018 [P] Create `src/CardStatement.Api/Contracts/StatementHeaderDto.cs` (`CardType`, `MaskedAccount`, `Period`, `PageCount`) per `openapi.yaml#/components/schemas/StatementHeader`.
- [x] T019 [P] Create `src/CardStatement.Api/Contracts/TransactionDto.cs` with ALL fields from `data-model.md` §1.4 including nullable `CategoryId`/`CategoryName`/`LabelId`/`LabelName` (`string?`) and `LabelUnmapped` (`bool`).
- [x] T020 [P] Create `src/CardStatement.Api/Contracts/SectionTotalsDto.cs` with `ComputedCharges`, `ComputedCredits`, `PrintedCharges` (`decimal?`), `PrintedCredits` (`decimal?`).
- [x] T021 [P] Create `src/CardStatement.Api/Contracts/StatementTotalsDto.cs` with `ComputedExpense`, `ComputedIncome`, `PrintedExpense` (`decimal?`), `PrintedIncome` (`decimal?`).
- [x] T022 [P] Create `src/CardStatement.Api/Contracts/CardholderSectionDto.cs` with `CardLast4`, `RawName`, `Transactions`, `Totals`, `ReconciliationStatus` (string enum).
- [x] T023 [P] Create `src/CardStatement.Api/Contracts/ExtractedStatementResponse.cs` with `Statement`, `Sections`, `Totals`, `ReconciliationStatus`, `NeedsReviewCount`, `UnmappedCards` (always `Array.Empty<string>()` in this iteration).
- [x] T024 [P] Create `src/CardStatement.Api/Contracts/ExtractionErrorResponse.cs` with `Error` containing `Code` and `Message`.
- [x] T025 [P] Create `src/CardStatement.Api/Contracts/ErrorCodes.cs` with a `public static class ErrorCodes` exposing string constants for all 7 codes: `INVALID_FILE_TYPE`, `EMPTY_FILE`, `FILE_TOO_LARGE`, `PASSWORD_PROTECTED`, `NO_TEXT_EXTRACTABLE`, `UNRECOGNIZED_LAYOUT`, `PARSE_FAILED`.

### Backend mapper & upload helper

- [x] T026 Create `src/CardStatement.Api/Mapping/StatementMapper.cs` skeleton with `public static ExtractedStatementResponse ToResponse(Statement statement)` returning a fully-default response (sections mapped to empty `transactions`/`totals` placeholders). Real mapping logic is added per user story.
- [x] T027 Create `src/CardStatement.Api/Endpoints/TempPdfFile.cs` — an `IDisposable` helper that writes an `IFormFile` to `Path.GetTempFileName()` and deletes the file on `Dispose`. Used by the endpoint to bridge `IFormFile` → `IPdfExtractor.Extract(string)` (per `research.md` §R7).

### Frontend foundation (parallelizable — separate files)

- [x] T028 [P] Create `frontend/src/types/api.ts` mirroring `contracts/openapi.yaml`: `ReconciliationStatus`, `RowType`, `Direction`, `StatementPeriod`, `StatementHeader`, `Transaction`, `SectionTotals`, `StatementTotals`, `CardholderSection`, `ExtractedStatementResponse`, `ExtractionErrorResponse`, plus a `Result = { ok: true; data: ExtractedStatementResponse } | { ok: false; error: ExtractionErrorResponse['error']; httpStatus: number }` discriminated union.
- [x] T029 [P] Create `frontend/src/api/statementsClient.ts` exporting `async function extractStatement(file: File): Promise<Result>` that POSTs `multipart/form-data` to `${import.meta.env.VITE_API_BASE_URL}/api/statements/extract` and returns the discriminated `Result` (maps non-OK HTTP responses to `{ ok: false, error, httpStatus }`).
- [x] T030 [P] Rewrite `frontend/src/App.tsx` with a `useReducer` state machine `{ kind: 'idle' } | { kind: 'uploading' } | { kind: 'success'; data } | { kind: 'error'; error; httpStatus }` and layout slots for `<UploadForm>`, `<StatementHeader>`, `<CardholderSection[]>`, statement totals, and `<ErrorBanner>` (components stubbed inline as `null`-returning placeholders for now).
- [x] T031 [P] Create `frontend/src/styles.css` with base typography, container width, and CSS variables for `--income`, `--expense`, `--mismatch`. Import once from `main.tsx`.
- [x] T032 [P] Create `frontend/index.html` title "WalletSeed — Statement Extract" (overwrite Vite default) and remove the React+Vite boilerplate logo from `frontend/src/App.tsx`.

**Checkpoint**: All DTO contracts compile. `Program.cs` has DI + JSON + CORS + Kestrel + logging configured but no endpoint yet. Frontend renders an empty shell at `http://localhost:5173`. Ready for user-story work.

---

## Phase 3: User Story 1 — Upload PDF, see every transaction (Priority: P1) 🎯 MVP

**Goal**: A user uploads the sample BAC statement PDF and sees the statement header plus every transaction grouped by cardholder section. **This is the MVP** — totals and error UX are deferred to US2/US3 but the MVP is shippable without them.

**Independent Test**: With the API and frontend running locally, upload `samples/final5140_45178439_316493_0.pdf`. The header (`VISA INFINITE BLACK`, `4593-78XX-XXXX-2145`, period `2026-05-21 → 2026-05-18`, pageCount `5`) and every transaction from `result.json` are visible in a table grouped by `cardLast4`. The same person appearing under two cards (e.g. `...2706` and `...5468` for FERNANDO MAGAÑA) produces two separate sections. Verified by the backend parity test `HappyPath_AllTransactions_RowForRow` (SC-002) and the frontend integration test `App.happy.integration.test.tsx`.

### Backend implementation — US1

- [x] T033 [US1] In `src/CardStatement.Api/Mapping/StatementMapper.cs`, implement the **transaction-level mapping** populating `date`, `postingDate`, `referenceNumber`, `sequenceCode`, `rowType` (enum→camelCase string), `description`, `amount`, `direction`, `cardLast4`, `needsReview`. Set `categoryId`/`categoryName`/`labelId`/`labelName` to `null` and `labelUnmapped` to `false` for every row.
- [x] T034 [US1] In `src/CardStatement.Api/Mapping/StatementMapper.cs`, implement the **section-level mapping** populating `cardLast4`, `rawName`, and the `transactions` array. Leave `totals` as default zeros and `reconciliationStatus` as `"ok"` (real values come in US2).
- [x] T035 [US1] In `src/CardStatement.Api/Mapping/StatementMapper.cs`, implement the **root-level mapping** populating `statement` (header + period + pageCount), `sections`, `needsReviewCount` (count of `Transactions.needsReview == true` across all sections), and `unmappedCards = Array.Empty<string>()`. Leave `totals` zeros and root `reconciliationStatus = "ok"` (US2 owns these).
- [x] T036 [US1] Create `src/CardStatement.Api/Endpoints/ExtractEndpoint.cs` exporting `static class ExtractEndpoint` with `MapExtract(this IEndpointRouteBuilder app)` that registers `POST /api/statements/extract` with `.DisableAntiforgery()`. Handler signature: `async (IFormFile file, IPdfExtractor pdf, IStatementParser parser, IReconciler reconciler, ILogger<...> log) => ...`. Implementation: wrap with `using var temp = new TempPdfFile(file);`, call `pdf.Extract(temp.Path) → parser.Parse → reconciler.Reconcile`, return `Results.Ok(StatementMapper.ToResponse(...))`. **No error guards yet — they belong to US3.**
- [x] T037 [US1] In `src/CardStatement.Api/Program.cs`, call `app.MapExtract();` after `app.UseCors(...)`.

### Backend tests — US1 (sequential within `ExtractEndpointTests.cs`)

- [x] T038 [P] [US1] Create `tests/CardStatement.Api.Tests/Fixtures/SamplePdf.cs` exposing `static class SamplePdf` with `Path` (`../../../../../samples/final5140_45178439_316493_0.pdf` resolved to absolute) and `OpenRead()` helper.
- [x] T039 [P] [US1] Create `tests/CardStatement.Api.Tests/Fixtures/GroundTruth.cs` that loads and deserializes `/result.json` into an `expected` model used by parity assertions.
- [x] T040 [P] [US1] Create `tests/CardStatement.Api.Tests/WebApiFactory.cs` (a `WebApplicationFactory<Program>` wrapper used by all tests).
- [x] T041 [US1] Create `tests/CardStatement.Api.Tests/ExtractEndpointTests.cs` with constructor injecting `WebApiFactory`, and add `HappyPath_Returns200_WithStatementHeader` asserting the response's `statement.cardType`, `maskedAccount`, `period.issueDate`, `period.cutoffDate`, `pageCount` match `result.json:statement.*`.
- [x] T042 [US1] Assert deep equality against `result.json:records[*]` field-by-field (`date`, `description`, `direction`, `amount`, `cardLast4`, `needsReview`). **This is the SC-002 gate.**
- [x] T043 [US1] Append `HappyPath_AttributesTransactionsToCorrectSection` to `tests/CardStatement.Api.Tests/ExtractEndpointTests.cs`: assert each `cardLast4` appears as its own section, FERNANDO MAGAÑA appears under both `2706` and `5468` as separate sections, and every transaction's parent section's `cardLast4` matches the transaction's `cardLast4`.

### Frontend implementation — US1

- [x] T044 [P] [US1] Create `frontend/src/components/UploadForm.tsx`: `<input type="file" accept="application/pdf,.pdf">`, "Extract" button, calls `props.onSubmit(file)`. No size/MIME pre-validation yet (US3 adds it).
- [x] T045 [P] [US1] Create `frontend/src/components/StatementHeader.tsx` rendering `cardType`, `maskedAccount`, `period.issueDate → period.cutoffDate`, `pageCount`.
- [x] T046 [P] [US1] Create `frontend/src/components/TransactionRow.tsx` rendering all FR-017 fields (`date`, `postingDate`, `referenceNumber`, `sequenceCode`, `description`, `amount`, `direction`) and a visual `income`/`expense` badge driven by `direction`.
- [x] T047 [US1] Create `frontend/src/components/CardholderSection.tsx`: section header showing `cardLast4` + `rawName`, a `<table>` of `<TransactionRow>` rows. **Leaves a totals slot empty** — US2 fills it.
- [x] T048 [US1] Wire `frontend/src/App.tsx`: on `UploadForm.onSubmit`, transition state to `uploading`, call `extractStatement(file)`, transition to `success` (render `<StatementHeader>` + `<CardholderSection>` per section) or `error` (placeholder until US3).
- [x] T049 [US1] Extend `frontend/src/styles.css` with table layout, sticky header, `.direction--income` / `.direction--expense` badge styles, section dividers.

### Frontend tests — US1

- [x] T050 [P] [US1] Create `frontend/tests/UploadForm.test.tsx`: renders, selecting a PDF enables the submit button, clicking submit calls `onSubmit` with the selected file.
- [x] T051 [P] [US1] Create `frontend/tests/App.happy.integration.test.tsx`: mock `statementsClient.extractStatement` to return a canned `ExtractedStatementResponse` matching the sample, drive the upload flow with `userEvent`, assert `<StatementHeader>` content and all sections' `cardLast4` + at least one `description` per section are rendered.

**Checkpoint — MVP demoable**: User uploads the sample PDF and sees the header + every transaction grouped by section. `ExtractEndpointTests.HappyPath_AllTransactions_RowForRow` is green. Independently shippable.

---

## Phase 4: User Story 2 — See per-section and overall totals (Priority: P2)

**Goal**: Show, for each cardholder section and for the whole statement, the computed sum (from extracted rows) and the printed sum (from the PDF's own `SUBTOTAL.:` / `TOTAL ...:`) side by side, with mismatches highlighted.

**Independent Test**: With US1 complete, upload the sample PDF. Each section shows two totals lines (computed and printed) that match to the cent. The statement footer shows total expense `1462.19` and total income `877.01` matching `result.json:totals.expense`/`totals.income`. Reconciliation status badge is `OK`. Verified by `HappyPath_SectionTotals_MatchPrintedAndComputed`, `HappyPath_StatementTotals_MatchResultJson`, `HappyPath_ReconciliationStatus_Ok`, and `TotalsPair.test.tsx`.

### Backend implementation — US2 (all touch `StatementMapper.cs` — sequential)

- [x] T052 [US2] In `src/CardStatement.Api/Mapping/StatementMapper.cs`, populate `CardholderSectionDto.Totals` per section: `computedCharges` = `Sum(t.Amount where t.Direction == Expense)`, `computedCredits` = `Sum(t.Amount where t.Direction == Income)`, `printedCharges` / `printedCredits` from `CardholderSection.PrintedSubtotals` in Core (use `null` if absent).
- [x] T053 [US2] In `src/CardStatement.Api/Mapping/StatementMapper.cs`, populate root `StatementTotalsDto`: `computedExpense` = sum across sections of `computedCharges`, `computedIncome` = sum across sections of `computedCredits`, `printedExpense` / `printedIncome` from `Statement.PrintedTotal*` in Core.
- [x] T054 [US2] In `src/CardStatement.Api/Mapping/StatementMapper.cs`, map `CardholderSection.ReconciliationStatus` and `Statement.ReconciliationStatus` from Core's `ReconciliationStatus` enum to the camelCase string `"ok"` / `"needsReview"` on both per-section and root levels.

### Backend tests — US2 (parallelizable — separate test methods can be added in parallel-ish but all live in the same file; sequence them)

- [x] T055 [US2] Append `HappyPath_SectionTotals_MatchPrintedAndComputed` to `tests/CardStatement.Api.Tests/ExtractEndpointTests.cs`: for each section, assert `computedCharges == printedCharges` and `computedCredits == printedCredits` (when printed values are present) to the cent.
- [x] T056 [US2] Append `HappyPath_StatementTotals_MatchResultJson` to `tests/CardStatement.Api.Tests/ExtractEndpointTests.cs`: assert `totals.computedExpense == 1462.19m` and `totals.computedIncome == 877.01m` (from `result.json:totals`). Also assert printed values match computed.
- [x] T057 [US2] Append `HappyPath_ReconciliationStatus_Ok` to `tests/CardStatement.Api.Tests/ExtractEndpointTests.cs`: assert root `reconciliationStatus == "ok"` and every section's `reconciliationStatus == "ok"` for the sample PDF.

### Frontend implementation — US2

- [x] T058 [P] [US2] Create `frontend/src/components/TotalsPair.tsx` taking `{ computed: number; printed: number | null; kind: 'charges' | 'credits' | 'expense' | 'income' }` and rendering both side by side, applying `.totals-mismatch` class when `printed !== null && Math.abs(computed - printed) > 0.005`.
- [x] T059 [US2] In `frontend/src/components/CardholderSection.tsx`, render two `<TotalsPair>` (charges, credits) below the table, fed from `section.totals`.
- [x] T060 [US2] In `frontend/src/App.tsx`, render a statement-footer block with two `<TotalsPair>` (expense, income) plus the root `reconciliationStatus` badge, fed from `response.totals` and `response.reconciliationStatus`.
- [x] T061 [US2] Extend `frontend/src/styles.css` with `.totals` grid layout, `.totals-mismatch` highlight (use `--mismatch` variable), and `.reconciliation-badge` styles for `ok` / `needsReview`.

### Frontend tests — US2

- [x] T062 [P] [US2] Create `frontend/tests/TotalsPair.test.tsx`: when `computed == printed`, the mismatch class is absent; when they differ by > 0.005, the mismatch class is applied; when `printed === null`, only the computed value renders and no mismatch styling shows.

**Checkpoint**: User can verify extraction quality at a glance via side-by-side totals. SC-003 verified by backend tests.

---

## Phase 5: User Story 3 — Clear, actionable errors when the PDF can't be parsed (Priority: P3)

**Goal**: Each of the four error categories (non-PDF, scanned-only PDF, unrecognized layout, password-protected) yields a distinct, human-readable error message; the app remains immediately usable for another upload.

**Independent Test**: Upload each of (a) a text file renamed `.pdf`, (b) a scanned-only PDF, (c) a PDF from a different bank, (d) a password-protected PDF. Each shows a distinct message and the UploadForm is immediately re-usable. Verified by six backend error tests in `ExtractEndpointTests.cs` and two frontend integration tests.

### Backend implementation — US3

- [x] T063 [US3] In `src/CardStatement.Api/Endpoints/ExtractEndpoint.cs`, add preflight checks at the top of the handler: if `file is null` return `Results.BadRequest(new ExtractionErrorResponse(...INVALID_FILE_TYPE...))`; if `file.Length == 0` return `EMPTY_FILE`; if `file.Length > config.Upload.MaxBytes` return `Results.Json(...FILE_TOO_LARGE..., statusCode: 413)`.
- [x] T064 [US3] In `src/CardStatement.Api/Endpoints/ExtractEndpoint.cs`, add a magic-byte sniff: open `file.OpenReadStream()`, read first 5 bytes, return `INVALID_FILE_TYPE` (400) if they are not `0x25 0x50 0x44 0x46 0x2D` (`%PDF-`). Reset the stream before passing to `TempPdfFile`.
- [x] T065 [US3] Create `src/CardStatement.Api/ErrorHandling/ExtractionFailureMapper.cs` exposing `public static IResult? TryMapKnown(Exception ex)` returning a structured error for: PdfPig encryption exception → 422 `PASSWORD_PROTECTED`; an internal `NoExtractableTextException` (define alongside) → 422 `NO_TEXT_EXTRACTABLE`; an internal `UnrecognizedLayoutException` → 422 `UNRECOGNIZED_LAYOUT`. Returns `null` for unknown.
- [x] T066 [US3] In `src/CardStatement.Api/Endpoints/ExtractEndpoint.cs`, wrap the parse call with a `try/catch`: post-extract, if `words.Count == 0` throw `NoExtractableTextException`; post-parse, if `statement.Sections.Count == 0 && statement.Sections.SelectMany(s => s.Transactions).Any() == false` throw `UnrecognizedLayoutException`. In the `catch (Exception ex)` block call `ExtractionFailureMapper.TryMapKnown(ex)`; on `null`, log the exception at `Error` level (NO PDF bytes, NO descriptions) and return 500 `PARSE_FAILED` with a generic message.
- [x] T067 [US3] In `src/CardStatement.Api/Program.cs`, ensure unhandled exceptions outside the endpoint also map to 500 `PARSE_FAILED` via `app.UseExceptionHandler(...)` returning the same `ExtractionErrorResponse` shape.

### Backend test fixtures + tests — US3

- [x] T068 [P] [US3] Add `tests/CardStatement.Api.Tests/Fixtures/Errors/empty.pdf` (zero-byte file), `plaintext.pdf` (the literal string `"not a pdf at all"`), `bad-magic.pdf` (starts with `XXXXX` then random bytes). Commit with `Content Include="..." CopyToOutputDirectory="PreserveNewest"` in the test csproj.
- [x] T069 [P] [US3] Add `tests/CardStatement.Api.Tests/Fixtures/Errors/scanned-no-text.pdf` (an image-only PDF — generate via ImageMagick or commit a known-good scanned sample) and `wrong-bank-layout.pdf` (any non-BAC text-based statement). Commit with `CopyToOutputDirectory="PreserveNewest"`.
- [x] T070 [P] [US3] Add `tests/CardStatement.Api.Tests/Fixtures/Errors/encrypted.pdf` (password-protected; generate with `qpdf --encrypt user owner 256 -- in.pdf encrypted.pdf` or commit a known-good encrypted sample). Commit with `CopyToOutputDirectory="PreserveNewest"`.
- [x] T071 [US3] In `tests/CardStatement.Api.Tests/ExtractEndpointTests.cs`, append **client-error 4xx tests**: `Error_InvalidFileType_PlainText_Returns400`, `Error_InvalidFileType_BadMagic_Returns400`, `Error_EmptyFile_Returns400`, `Error_FileTooLarge_Returns413`. Each asserts the HTTP status, the JSON shape `{ error: { code, message } }`, and the exact `code` value.
- [x] T072 [US3] In `tests/CardStatement.Api.Tests/ExtractEndpointTests.cs`, append **content 422 tests**: `Error_PasswordProtected_Returns422`, `Error_NoTextExtractable_Returns422`, `Error_UnrecognizedLayout_Returns422`. Each asserts HTTP status `422`, JSON shape, and exact `code` value.

### Frontend implementation — US3

- [x] T073 [P] [US3] Create `frontend/src/components/ErrorBanner.tsx` taking `{ error: { code: string; message: string }; httpStatus: number }` and rendering a human-readable banner. Map known codes to friendlier message overrides (fall back to `error.message`): `INVALID_FILE_TYPE` → "Please upload a PDF file.", `EMPTY_FILE` → "The selected file is empty.", `FILE_TOO_LARGE` → "This file exceeds the 25 MB limit.", `PASSWORD_PROTECTED` → "This PDF is password-protected. Please remove the password and try again.", `NO_TEXT_EXTRACTABLE` → "This PDF doesn't contain machine-readable text. Scanned PDFs aren't supported in this version.", `UNRECOGNIZED_LAYOUT` → "We couldn't recognize this as a BAC Credomatic statement.", `PARSE_FAILED` → "Something went wrong while reading this PDF. Please try again."
- [x] T074 [US3] In `frontend/src/components/UploadForm.tsx`, add preflight validation in the submit handler: if `file.type !== 'application/pdf'` and the extension isn't `.pdf` → emit a synthetic `INVALID_FILE_TYPE` error via `props.onLocalError` (don't call the API); if `file.size > 25 * 1024 * 1024` → emit a synthetic `FILE_TOO_LARGE` error. Don't transition into `uploading`.
- [x] T075 [US3] In `frontend/src/App.tsx`, when state is `error`, render `<ErrorBanner>` above `<UploadForm>` and leave `<UploadForm>` enabled and reset (file input cleared) so the user can immediately retry. Wire `UploadForm.onLocalError` to dispatch into the `error` state.
- [x] T076 [US3] Extend `frontend/src/styles.css` with `.error-banner` styles (warning color, dismiss-on-new-upload behavior is structural, not CSS).

### Frontend tests — US3

- [x] T077 [P] [US3] Create `frontend/tests/UploadForm.errors.test.tsx`: selecting a non-PDF file then clicking submit triggers `onLocalError` with `code: 'INVALID_FILE_TYPE'` and does NOT call the API client; selecting a >25 MB file triggers `onLocalError` with `code: 'FILE_TOO_LARGE'`.
- [x] T078 [P] [US3] Create `frontend/tests/App.errors.integration.test.tsx`: mock `statementsClient.extractStatement` to return each error code in turn; assert the rendered `ErrorBanner` text differs for each code; assert `<UploadForm>` is still present and enabled after the error renders.

**Checkpoint**: All four error categories from User Story 3 are distinguishable on screen. SC-006 verified.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Verify cross-cutting requirements (logging redaction, determinism, end-to-end timing) and update docs.

- [x] T079 [P] Audit `src/CardStatement.Api/Program.cs` and `src/CardStatement.Api/Endpoints/ExtractEndpoint.cs` for the R9 logging policy: confirm only `filename` (basename), `file.Length`, `pageCount`, total row count, and per-section reconciliation status are logged at `Information`; raw PDF bytes, full transaction descriptions, amounts, dates, masked-account, and cardholder names are NOT logged at default level.
- [x] T080 [P] Update root `README.md` to add an "API + Frontend (spec 001)" section linking to `specs/001-pdf-extract-web/quickstart.md` and noting the new `dotnet run --project src/CardStatement.Api` + `cd frontend && pnpm dev` workflow. Do not remove existing console-PoC documentation.
- [x] T081 Manually run the `quickstart.md` §3 happy-path walkthrough and time end-to-end upload → table render against the sample PDF. Confirm `< 10 s` (SC-001).
- [x] T082 Manually verify SC-007 determinism: upload `samples/final5140_45178439_316493_0.pdf` twice in a row in the same browser session; assert via browser devtools that the two JSON responses are byte-identical (`Network → Response → Copy as JSON`, then `diff`).
- [x] T083 Manually verify SC-006: run through all four error scenarios from `quickstart.md` §4 and confirm each produces a distinct user-facing message and the form is immediately re-usable.
- [x] T084 Confirm `src/CardStatement.App/appsettings.Development.json` (existing) was not accidentally extended; the new API has no secrets to manage in this iteration.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)** — no dependencies; start immediately.
- **Phase 2 (Foundational)** — depends on Phase 1; **blocks all user stories**.
- **Phase 3 (US1, MVP)** — depends on Phase 2. **Independently shippable** — totals can show as zeros and errors as raw JSON until US2/US3 ship.
- **Phase 4 (US2)** — depends on Phase 2; can run in parallel with US3. Extends `StatementMapper`, `CardholderSection.tsx`, `App.tsx`.
- **Phase 5 (US3)** — depends on Phase 2; can run in parallel with US2. Extends `ExtractEndpoint.cs`, `UploadForm.tsx`, `App.tsx`.
- **Phase 6 (Polish)** — depends on all included user stories being complete.

### Within Each User Story

- Backend mapper changes → backend endpoint registration → backend tests.
- Frontend components → frontend wiring in `App.tsx` → frontend tests.
- Tests must turn green before the story's checkpoint is claimed.

### Cross-story note

US2 and US3 both edit `frontend/src/App.tsx` (US2 adds a statement-footer block, US3 adds an `ErrorBanner` slot). If both are worked in parallel, expect a small merge in `App.tsx` and `frontend/src/styles.css`. Backend US2 and US3 touch different files (`StatementMapper.cs` vs. `ExtractEndpoint.cs` + new files) — no conflict.

### Parallel Opportunities

- **Phase 1**: T006–T011 can all run in parallel (different files / independent commands).
- **Phase 2 DTOs**: T017–T025 are all separate files → all `[P]`. T028–T032 are separate frontend files → all `[P]`.
- **Phase 3 US1**: T038–T040 (fixtures) can run in parallel; T044–T046 (frontend components) can run in parallel. The shared-file tasks (T041–T043 on `ExtractEndpointTests.cs`, T033–T035 on `StatementMapper.cs`) are sequential.
- **Phase 5 US3**: T068–T070 (error fixtures), T077–T078 (frontend tests), and T073 (`ErrorBanner.tsx`) parallelize cleanly with each other.

---

## Parallel Example: User Story 1 (Phase 3)

```bash
# Once Phase 2 is done, kick off these in parallel:

# Stream A: backend test fixtures (independent files)
Task: "T038 [P] [US1] Create tests/CardStatement.Api.Tests/Fixtures/SamplePdf.cs"
Task: "T039 [P] [US1] Create tests/CardStatement.Api.Tests/Fixtures/GroundTruth.cs"
Task: "T040 [P] [US1] Create tests/CardStatement.Api.Tests/WebApiFactory.cs"

# Stream B: frontend components (independent files)
Task: "T044 [P] [US1] Create frontend/src/components/UploadForm.tsx"
Task: "T045 [P] [US1] Create frontend/src/components/StatementHeader.tsx"
Task: "T046 [P] [US1] Create frontend/src/components/TransactionRow.tsx"

# Stream C: frontend tests (independent files)
Task: "T050 [P] [US1] Create frontend/tests/UploadForm.test.tsx"
Task: "T051 [P] [US1] Create frontend/tests/App.happy.integration.test.tsx"
```

The mapper tasks (T033–T035) and endpoint test methods (T041–T043) are sequential because they share files.

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete **Phase 1** (T001–T011) — scaffolds exist, projects compile.
2. Complete **Phase 2** (T012–T032) — DTOs defined, DI wired, frontend shell renders.
3. Complete **Phase 3 / User Story 1** (T033–T051).
4. **STOP and VALIDATE**: backend `dotnet test tests/CardStatement.Api.Tests` is green (especially `HappyPath_AllTransactions_RowForRow` — the SC-002 gate); frontend `pnpm test --run` is green; manual upload of the sample PDF in the browser shows the header and every transaction grouped by section.
5. **Ship the MVP** — totals are zero-valued and errors render as raw JSON, but the core user value (extract + display) is live.

### Incremental Delivery

1. After MVP ships, add **User Story 2** (totals) — one focused PR.
2. Then add **User Story 3** (errors + preflight) — another focused PR.
3. **Phase 6 polish** — only after US2 and US3 are merged.

### Parallel Team Strategy

If two contributors are available after Phase 2:

- Contributor A: User Story 1 (MVP). Must finish before US2 has anything to extend.
- Once US1 is done:
  - Contributor A: User Story 2.
  - Contributor B: User Story 3.

Heads-up: US2 and US3 both edit `frontend/src/App.tsx` and `frontend/src/styles.css` — coordinate small merges.

---

## Notes

- `[P]` = different files, no dependency on incomplete tasks.
- `[Story]` label maps the task to the user story it advances; Setup/Foundational/Polish carry no story label.
- Each user story closes with a Checkpoint that is independently demoable.
- Backend tests against `result.json` and the sample PDF are the source of truth for SC-002, SC-003, and SC-005.
- Avoid the trap of expanding the spec inside `tasks.md` — if a task starts feeling like it needs a new requirement, stop and revise `spec.md` first.
