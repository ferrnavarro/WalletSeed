# Implementation Plan: Wallet Import from BAC PDF Statements

**Branch**: `003-wallet-import` | **Date**: 2026-06-25 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/003-wallet-import/spec.md`

## Summary

Add a second user-facing page to the existing frontend — **Wallet Import** at route `/wallet-import` — and a new outbound integration in the backend that proxies the external **Wallet** budget-tracker API. The new flow uploads a BAC PDF (reusing the existing extraction pipeline from `001`/`002`), fetches the user's existing Wallet records for the statement period (±5 days), computes a per-PDF-row duplicate match against those records (±2 days + exact amount + same currency + same direction), shows both sides in a comparison table, lets the user select rows + assign a category per row, and submits the chosen rows to Wallet in batches of ≤50 with a per-row outcome surfaced back to the UI. The Wallet JWT lives only on the backend (`appsettings`/env); the browser never sees it. Labels are auto-resolved from a backend-config map of **PDF cardholder section `RawName` → Wallet label id**.

The shape that keeps this cheap: one **outbound** Wallet HTTP client (`IWalletApiClient` typed `HttpClient`) bundling auth + paging + chunking, one **stateless** orchestrator (`WalletImportService`) that the new endpoints depend on, two new minimal-API endpoints under `/api/wallet/...` reusing the existing `IBankResolver` for PDF extraction, and a minimal frontend addition (one new route, one new page, one new API client). Zero edits to the existing `/api/statements/extract` endpoint contract, zero edits to the Statement model, zero edits to the existing frontend extract page.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (matches existing `global.json` SDK `10.0.201` and `Directory.Build.props` `<TargetFramework>net10.0</TargetFramework>`) for the backend. TypeScript ~6.0 + React 19 + Vite 8 for the frontend. No language or runtime change.
**Primary Dependencies**: Backend reuses what's already present (`UglyToad.PdfPig`, `Microsoft.Extensions.*`, ASP.NET Core Minimal API). Adds **`Microsoft.Extensions.Http`** (already implicit via ASP.NET Core) for `IHttpClientFactory` + typed clients. Frontend adds **`react-router-dom@7`** (smallest pinch of routing — single `<BrowserRouter>` + two `<Route>`s + a `<NavLink>`). No new test frameworks.
**Storage**: None. Stateless backend; carry forward from `001`/`002`. The Wallet JWT lives in `appsettings.json` (or env var via `Wallet__Jwt`), read at request time so a hot-edit-and-restart cycle picks it up without rebuild. The cardholder-section → label-id map lives in the same `Wallet` config section.
**Testing**: xUnit (existing `tests/CardStatement.Tests`, `tests/CardStatement.Api.Tests`). Vitest + React Testing Library + `jsdom` (existing) for the frontend. Backend's outbound Wallet calls are tested with **a single fake `HttpMessageHandler`** that records requests and returns canned responses — no real Wallet calls in tests.
**Target Platform**: Backend on macOS/Linux developer machines (localhost, port `5080`, same as `001`/`002`). Frontend at `http://localhost:5173`. Unchanged from `001`/`002`.
**Project Type**: web application (backend HTTP API + React frontend). Both trees exist already.
**Performance Goals**: End-to-end "click Compare → see comparison view" under **5 s** for a statement period that contains ≤ 200 existing Wallet records (one extraction + at most ~7 paginated GETs at limit=30 = 7 sequential HTTPS round trips inside the backend, plus the existing extraction's sub-second cost). End-to-end "click Import → see per-row outcomes" under **5 s** for batches of ≤ 50 selected rows (one POST). These targets are envelopes, not hard SLAs — the bottleneck is the Wallet API's response time, which we do not control.
**Constraints**: Stateless across requests. Deterministic comparison output for the same `(PDF, Wallet records, account)` input. Wallet JWT MUST NOT be logged, MUST NOT be sent to the frontend, MUST NOT appear in any response body. Per-row description text MUST NOT be logged at default log level (existing privacy constraint from `001`). Wallet endpoints must NOT block backend startup when JWT is missing — only those endpoints fail with a structured `WALLET_NOT_CONFIGURED` response; the existing `/api/statements/extract` keeps working.
**Scale/Scope**: Single-user / small trusted-collaborator group on localhost. A typical BAC statement has 20–100 transactions; a typical Wallet account has 30–200 records in any given 30-day window. The batch chunk size (≤50) is a Wallet-imposed ceiling, not ours.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The project constitution at `.specify/memory/constitution.md` is **unratified** (unmodified Speckit template with placeholder principles). No concrete gates to evaluate.

**Status**: PASS (vacuously — no ratified principles to violate).

Carrying forward the five recommended principles from `001`/`002`:

1. **Deterministic extraction** — every part of the new flow (PDF parse, dedup match, render order) MUST be repeatable for the same input.
2. **Reuse `CardStatement.Core`** — the new flow consumes the existing `IBankResolver`; it does not introduce a parallel parse path.
3. **Stateless services** — no session state between Compare and Submit; the frontend resends what it wants to import.
4. **Honest errors** — Wallet-side failures get their own structured error codes; we do not overload the existing `UNRECOGNIZED_LAYOUT` / `PARSE_FAILED` taxonomy with networking failures.
5. **One narrow seam per variation point** (added in `002`) — Wallet integration sits behind one typed HTTP client (`IWalletApiClient`); the orchestrator (`WalletImportService`) and endpoints depend only on the interface, never on `HttpClient` directly.

All five are honored by this plan. No constitution gates fail.

## Project Structure

### Documentation (this feature)

```text
specs/003-wallet-import/
├── plan.md              # This file (/speckit-plan command output)
├── spec.md              # /speckit-specify output (already exists)
├── research.md          # Phase 0 output (this command)
├── data-model.md        # Phase 1 output (this command)
├── quickstart.md        # Phase 1 output (this command)
├── contracts/
│   └── openapi.yaml     # Phase 1 output (this command) — additive diff vs 002
├── checklists/
│   └── requirements.md  # /speckit-specify output (already exists)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── CardStatement.Core/                              # UNCHANGED — bank-agnostic, no Wallet coupling
│   ├── Models/Transaction.cs                        # UNCHANGED
│   ├── Models/CardholderSection.cs                  # UNCHANGED
│   ├── Banks/Bac/                                   # UNCHANGED
│   └── ...                                          # UNCHANGED
│
├── CardStatement.Api/
│   ├── Program.cs                                   # EDITED — registers Wallet client + Wallet options; registers Wallet endpoints
│   ├── appsettings.json                             # EDITED — adds "Wallet" section (BaseUrl, Jwt, LabelMapping, optional Timeout)
│   ├── Endpoints/
│   │   ├── ExtractEndpoint.cs                       # UNCHANGED — existing /api/statements/extract preserved byte-identical
│   │   └── WalletImportEndpoint.cs                  # NEW — maps GET /api/wallet/accounts, GET /api/wallet/categories, POST /api/wallet/import/compare, POST /api/wallet/import/submit
│   ├── Wallet/                                      # NEW FOLDER — all Wallet-integration code in one place
│   │   ├── WalletOptions.cs                         # NEW — IOptions-bound { BaseUrl, Jwt, LabelMapping: Dictionary<string,string>, TimeoutSeconds }
│   │   ├── IWalletApiClient.cs                      # NEW — narrow interface: ListAccountsAsync, ListCategoriesAsync, ListRecordsAsync(account, from, to), CreateRecordsAsync(batch)
│   │   ├── WalletApiClient.cs                       # NEW — typed HttpClient impl; handles paging (offset+limit), JWT injection, error mapping
│   │   ├── WalletApiException.cs                    # NEW — { Kind: CredentialsInvalid|Unavailable|Rejected|NotConfigured, HttpStatus?, BodyExcerpt }
│   │   ├── WalletImportService.cs                   # NEW — orchestrator: takes (PDF, accountId, rows-to-submit); reuses IBankResolver/IReconciler for extract
│   │   ├── DuplicateMatcher.cs                      # NEW — pure function: (pdfRows, walletRecords) → IReadOnlyDictionary<int, IReadOnlyList<string>> by FR-009 rule
│   │   ├── LabelMappingResolver.cs                  # NEW — reads WalletOptions.LabelMapping; resolves cardholder.RawName → labelIds
│   │   └── Contracts/                               # NEW FOLDER — DTOs for the new endpoints (separate from Extraction DTOs)
│   │       ├── WalletAccountDto.cs                  # NEW
│   │       ├── WalletCategoryDto.cs                 # NEW
│   │       ├── CompareResponse.cs                   # NEW — { pdfRows, walletRows, pairings, statementWindow, account, unmappedSections }
│   │       ├── PdfRowDto.cs                         # NEW — { index, date, signedAmount, currency, description, counterParty, cardholderSectionRawName, cardLast4, defaultSelected, currencyMismatch }
│   │       ├── WalletRowDto.cs                      # NEW — { id, date, signedAmount, currency, note, counterParty, categoryName, claimedByPdfIndices }
│   │       ├── PairingDto.cs                        # NEW — { pdfIndex, walletRecordIds[] }
│   │       ├── SubmitRequest.cs                     # NEW — { accountId, rows: [{ index, date, signedAmount, currency, description, counterParty, cardholderSectionRawName, categoryId }] }
│   │       ├── SubmitResponse.cs                    # NEW — { outcomes: [{ index, ok, walletRecordId?, errorMessage? }] }
│   │       └── WalletErrorResponse.cs               # NEW — { error: { code, message } } using a new code enum: WALLET_NOT_CONFIGURED, WALLET_CREDENTIALS_INVALID, WALLET_UNAVAILABLE, WALLET_REJECTED
│   ├── Wallet/Registration/
│   │   └── WalletServiceCollectionExtensions.cs     # NEW — AddWalletIntegration(IConfiguration): binds WalletOptions, registers IWalletApiClient as a typed HttpClient, registers DuplicateMatcher / LabelMappingResolver / WalletImportService
│   ├── Contracts/                                   # UNCHANGED (existing extraction DTOs remain; the wallet flow uses its own DTOs under Wallet/Contracts/ to avoid coupling)
│   └── ErrorHandling/                               # UNCHANGED
│
└── CardStatement.App/                               # UNCHANGED — CLI tool does not gain Wallet support in this iteration
    └── ...                                          # UNCHANGED

frontend/
├── package.json                                     # EDITED — adds dependency "react-router-dom": "^7.x"
├── src/
│   ├── main.tsx                                     # EDITED — wraps <App/> in <BrowserRouter>
│   ├── App.tsx                                      # EDITED — replaces single-page render with <Routes>: "/" → existing StatementExtractPage, "/wallet-import" → new WalletImportPage; adds a tiny top <Nav> with two NavLinks
│   ├── pages/                                       # NEW FOLDER
│   │   ├── StatementExtractPage.tsx                 # NEW — the existing content of App.tsx moved here verbatim (the existing extract flow becomes its own page)
│   │   └── WalletImportPage.tsx                     # NEW — orchestrates the new flow: AccountPicker → UploadForm → ComparisonView → SubmitOutcome
│   ├── components/                                  # EXTENDED
│   │   ├── Nav.tsx                                  # NEW — two-link header nav: "Statement Extract" / "Wallet Import"
│   │   ├── WalletAccountPicker.tsx                  # NEW — dropdown of accounts loaded from /api/wallet/accounts
│   │   ├── ComparisonTable.tsx                      # NEW — side-by-side PDF rows / Wallet rows with pairings, per-row checkbox + category dropdown
│   │   ├── CategoryDropdown.tsx                     # NEW — re-usable; consumes the categories list once
│   │   ├── SubmitOutcomeList.tsx                    # NEW — per-row success/failure UI after submit
│   │   ├── WalletErrorBanner.tsx                    # NEW — renders the 4 Wallet error codes with operator-actionable copy
│   │   └── (UploadForm.tsx / ErrorBanner.tsx / StatementHeader.tsx / TotalsPair.tsx / TransactionRow.tsx / CardholderSection.tsx) # UNCHANGED — still used by StatementExtractPage
│   ├── api/                                         # EXTENDED
│   │   ├── statementsClient.ts                      # UNCHANGED
│   │   └── walletClient.ts                          # NEW — listAccounts(), listCategories(), compare(file, accountId), submit(payload)
│   ├── types/                                       # EXTENDED
│   │   ├── api.ts                                   # UNCHANGED (existing extraction types)
│   │   └── wallet.ts                                # NEW — WalletAccount, WalletCategory, CompareResponse, PdfRow, WalletRow, Pairing, SubmitRequest, SubmitResponse, WalletErrorCode
│   └── styles.css                                   # EDITED — adds styles for nav + comparison table; existing rules UNCHANGED
└── tests/                                           # EXTENDED
    ├── walletImportPage.test.tsx                    # NEW — RTL: account pick + upload + comparison renders + submit happy path (msw-style fetch stubs)
    ├── duplicateMatcher.test.tsx                    # (lives backend-side instead — see below)
    └── (existing tests UNCHANGED)

tests/
├── CardStatement.Tests/
│   ├── Wallet/                                      # NEW FOLDER
│   │   ├── DuplicateMatcherTests.cs                 # NEW — table-driven: in-window/out-of-window dates, equal/inequal amounts, currency mismatch, direction mismatch, many-to-one and one-to-many
│   │   ├── LabelMappingResolverTests.cs             # NEW — match/no-match/case-sensitivity policy
│   │   └── WalletApiClientPagingTests.cs            # NEW — fake HttpMessageHandler: walks 3 pages until nextOffset is absent; asserts full result accumulation
│   └── (existing tests UNCHANGED)
│
└── CardStatement.Api.Tests/
    ├── Wallet/                                      # NEW FOLDER
    │   ├── WalletEndpointsTests.cs                  # NEW — WebApplicationFactory with a fake HttpMessageHandler injected: happy path Compare + Submit, paginated GET records, partial-failure submit, 401 → WALLET_CREDENTIALS_INVALID, network → WALLET_UNAVAILABLE
    │   ├── WalletNotConfiguredTests.cs              # NEW — boots backend with no Wallet:Jwt set; asserts (a) existing /api/statements/extract still works, (b) every /api/wallet/* returns WALLET_NOT_CONFIGURED
    │   └── WalletImportEndToEndTests.cs             # NEW — feeds the BAC sample PDF + a fake Wallet snapshot; asserts the compare response pairs the expected rows, then submits and asserts per-row outcomes
    └── (existing tests UNCHANGED)
```

**Structure Decision**: Extend the existing **web-application** layout from `001`/`002`. All Wallet-integration code lives inside the existing `CardStatement.Api` project under a new `Wallet/` folder — keeping it out of `CardStatement.Core` is deliberate: `Core` stays bank-agnostic and budget-tracker-agnostic, while `Api` is allowed to depend on outbound integrations because it's the host. The one most important structural decision is the new `Wallet/` folder paralleling the `Banks/` folder in `Core`: each external system the app talks to gets one folder, one typed client, one options class, one registration extension. The frontend adds exactly one route (`/wallet-import`), one page component (`WalletImportPage.tsx`), and one API client (`walletClient.ts`); the existing extract page becomes its own component under `pages/` instead of being inlined in `App.tsx`. The single biggest dependency added to either tree is **`react-router-dom`** on the frontend; the backend gains no new NuGet package.

## Phase 0: Outline & Research

See [research.md](./research.md) for the full write-up. Decisions resolved:

1. **Where Wallet-integration code lives in the backend** — chose `src/CardStatement.Api/Wallet/`, not a new project, not in `CardStatement.Core`. Rationale: `Core` is the bank-agnostic statement parser and must stay independent of outbound integrations; `Api` is the host and naturally owns the system's outbound dependencies. A new csproj is overkill for a single-page feature. Rejected: putting it in `Core` (couples parser to a budget tracker), spinning a `CardStatement.Wallet` project (build-time overhead, no boundary it would actually protect).

2. **Outbound HTTP shape** — chose a typed `IWalletApiClient` over a static `HttpClient` or a generated OpenAPI client. Rationale: only 4 endpoints are needed (accounts, categories, records GET, records POST), hand-writing the surface is smaller than tooling around the 6,931-line Wallet OpenAPI, and the typed client is trivially fakeable in tests via an `HttpMessageHandler`. Rejected: code-gen (`NSwag`/`Kiota`) — bigger toolchain dependency than the feature deserves; raw `HttpClient` injected into the orchestrator — leaks transport details into the service.

3. **Where the JWT lives + how it's injected** — chose: configured in `appsettings.json` under `Wallet:Jwt` (or env var `Wallet__Jwt`), bound to `WalletOptions` via `IOptions<>`, read **per request** by `WalletApiClient` to attach a `Bearer` Authorization header. Rationale: `appsettings`/env is the standard .NET secret pattern; reading per request means a config-file edit + restart picks up a new token without rebuild; reading per request also prevents a stale options snapshot from baking the value into the client. Rejected: hard-coding (obvious), `IOptionsMonitor` snapshotting (an unnecessary indirection for a value that only changes across restarts), `Authorization` header as a default request header on the typed client (would require recreating the client whenever options changed).

4. **Empty / missing JWT policy** — chose: backend starts normally regardless of whether `Wallet:Jwt` is set; the existing `/api/statements/extract` endpoint is not gated; only Wallet endpoints fail at request time with a structured `WALLET_NOT_CONFIGURED` error and HTTP 503. Rationale: the spec (FR-024) explicitly forbids Wallet config from being a global startup gate. The check is a tiny conditional in `WalletApiClient` that fires before any outbound call. Rejected: failing startup if JWT missing (breaks FR-024 and the extract page), returning 500 (caller can't tell it's a config problem), returning 401 (incorrect: the *backend* is not configured, the *user* isn't unauthenticated).

5. **Wallet error taxonomy** — chose a new 4-code enum on a **separate** error envelope (`WalletErrorResponse`) for Wallet-side failures: `WALLET_NOT_CONFIGURED` (503), `WALLET_CREDENTIALS_INVALID` (502), `WALLET_UNAVAILABLE` (504), `WALLET_REJECTED` (502). The existing `ExtractionErrorResponse` envelope and `ErrorCodes` set are reused unchanged for PDF-stage errors of the Compare endpoint. Rationale: clean separation in the frontend (the existing `ErrorBanner` handles extraction errors; a new `WalletErrorBanner` handles Wallet errors), no overloading of the `001`/`002` error codes (honors the spec's "do not pollute the existing taxonomy" assumption), and the HTTP statuses match what proxies traditionally use for upstream issues. Rejected: extending the existing `ErrorCodes` enum (breaks the additive-only contract from `002` openapi.yaml), using HTTP 401 for credentials-invalid (would imply the *caller* is unauthenticated, which is wrong — the *backend's* credentials are invalid).

6. **Wallet records pagination** — chose: backend loops `GET /v1/api/records?accountId=…&recordDate=gte.<from>&recordDate=lt.<to>&limit=200&offset=…` until `nextOffset` is absent in the response (Wallet's documented "no more pages" signal), accumulating results into one list. Limit chosen at the documented max (200) to minimize round trips. Rationale: the spec requires complete coverage before showing the comparison (FR-007); fewer round trips means less time spent waiting on the network. Rejected: `limit=30` (default) — needlessly tripled round trips on typical windows; `withTotal=true` (just asks Wallet to do extra work we don't need — pagination is signaled by `nextOffset` absence).

7. **Date-window math** — chose: window is `[period.cutoffDate - 5 days, period.cutoffDate + 5 days]` extended to also cover `[period.issueDate - 5 days, period.issueDate + 5 days]`, then unioned (so the resulting fetch is `[min(issueDate, cutoffDate) - 5d, max(issueDate, cutoffDate) + 5d]`). For BAC statements `issueDate ≤ cutoffDate` always, so this simplifies to `[issueDate - 5d, cutoffDate + 5d]`. Rationale: spec says ±5 days at each boundary (FR-007); union over the two boundaries gives the user a continuous range with no missed gap in the middle. Rejected: ±5 only at the cutoff (would miss early-period manual entries); a full statement period + ±5 day buffer (the same thing — calling it out explicitly).

8. **Duplicate rule encoding** — chose a pure function `DuplicateMatcher.Match(pdfRows, walletRecords) → Dictionary<pdfRowIndex, List<walletRecordId>>` operating on already-projected DTOs. Inputs are: `(date, signedAmount, currency)` per row on both sides; "direction" is derived from sign (negative → expense, positive → income); equality on `signedAmount` is exact (`decimal == decimal`), date window is `Math.Abs((pdf.date - wallet.recordDate).Days) <= 2`. Rationale: ports cleanly to the test table and is deterministic (it sorts pdfRows by their `index`, then sorts each pdfRow's matched wallet ids lexicographically). Rejected: any "fuzzy" amount tolerance (the spec is explicit about exact equality); attempting to detect "the same transaction posted in a different currency" (out of scope — would need FX conversion).

9. **Per-row currency on PDF side** — chose: every PDF row's currency in the compare response is set to the chosen Wallet account's `currencyCode`. Rationale: the current BAC parser does not extract per-row currency (`grep -i currency` over `CardStatement.Core/` returns no matches). Treating BAC SV statements as single-currency-per-account is consistent with how the existing parser models them. FR-010's currency-mismatch indicator is wired in the DTO (`currencyMismatch: bool`) but will never fire in this iteration; the field is left in so a future spec that adds per-row currency extraction to BAC's parser will not need to change the contract. Rejected: omitting the field (forces a contract change later); adding currency extraction to BAC's parser (out of this spec's scope — that's a `CardStatement.Core` change).

10. **PDF signed amount** — chose: `signedAmount = direction == Expense ? -amount : +amount`, computed once in the API mapper. Wallet API also signs amounts the same way (negative = expense). Rationale: by signing on the backend at projection time, the duplicate matcher only ever sees signed decimals and never has to know about `Direction` enums or column semantics. Rejected: passing `(amount, direction)` to the matcher and the frontend (more fields, two places to forget the sign convention).

11. **Default selection rule** — chose, exactly per spec: `defaultSelected = pairings[index].Count == 0 AND !currencyMismatch`. Rationale: directly implements FR-014 + FR-010's "default unselected for mismatch". The frontend renders this field rather than re-deriving the rule, so the rule lives in one place (the backend). Rejected: leaving the rule to the frontend — duplicates logic and makes regression tests harder.

12. **Where the cardholder→label mapping is read** — chose: `WalletOptions.LabelMapping` is a `Dictionary<string, string>` (`rawName → labelId`), loaded from `appsettings.json` (or env `Wallet__LabelMapping__<RawName>=<labelId>`) once per request via `IOptions<WalletOptions>`. Lookups are case-insensitive (`StringComparer.OrdinalIgnoreCase`) because BAC PDF cardholder names are uppercase but operators may transcribe them either way in config. A row whose cardholder has no entry gets `labelIds: []` and the section is reported in the response's `unmappedSections` list. Rationale: spec says no labels is allowed (not an error), but the UI must make it visible — the explicit `unmappedSections` list lets the frontend surface this once at the top, not per row. Rejected: per-row "unmapped" flag (noisy in the UI); throwing on unmapped (violates spec).

13. **Submit batching + per-row outcome** — chose: backend chunks the `SubmitRequest.rows` into groups of 50 in the order they arrived; calls `POST /v1/api/records` per chunk; maps each chunk's response (`200`, `207`, `400`, `401`) to per-row outcomes; concatenates outcomes preserving input row indices; returns one `SubmitResponse`. If any chunk returns `401`/`403`, the backend stops issuing subsequent chunks and marks **all remaining rows** as a single `WALLET_CREDENTIALS_INVALID` error — and the orchestrator surfaces this as the whole-response `error` envelope (not as per-row failures), because the spec says "report a single 'credentials invalid' outcome that the frontend renders as one banner" (FR-020). Rationale: matches the spec exactly. Rejected: stopping on the first per-row failure (regression of the explicit "row failures don't block other rows" rule).

14. **Where extraction happens for the Compare endpoint** — chose: Compare reuses the existing `IBankResolver` directly inside `WalletImportService`. The endpoint does the file-safety checks already in `ExtractEndpoint` (size, magic-bytes, empty), then hands the temp file's words to the resolver. Rationale: spec FR-005 says "MUST invoke the existing backend extraction flow rather than a parallel one — there is one source of truth"; we honor it by calling the same `IBankResolver` the existing endpoint calls. The duplicated guard code is extracted into a small `PdfUploadGuard` helper used by both endpoints, so the rules live in one place. Rejected: having the frontend hit `/api/statements/extract` first and then `/api/wallet/import/compare` second (two round trips, two file uploads, two sources of truth on errors).

15. **Frontend routing choice** — chose `react-router-dom@7`. Rationale: smallest, most ubiquitous React routing library; works out of the box with Vite + React 19; documentation matches what most engineers already know. Rejected: hand-rolled `useState`-based "screen" switcher (cheaper today, costs more the moment a third page is needed); TanStack Router (more powerful than this spec needs).

16. **`react-router` data fetching style** — chose: components fetch in `useEffect` and manage their own loading/error state (matching the existing `App.tsx` style). Rationale: zero new mental model for the project — same pattern as `statementsClient.ts` + `useReducer` already in place. Rejected: `react-router` loaders (would force restructuring routes around data; smallest-step approach prefers staying close to the existing pattern).

17. **Where the existing extract page goes** — chose: move the existing single-page content out of `App.tsx` into `pages/StatementExtractPage.tsx` (verbatim) and have `App.tsx` shrink to the `<BrowserRouter><Nav/><Routes>…</Routes></BrowserRouter>` skeleton. Rationale: cheapest split; preserves the entire existing extract flow without behavioural change; the only edit to existing code is `App.tsx` and `main.tsx`. Rejected: leaving the extract content in `App.tsx` and adding the Wallet page elsewhere (asymmetric and confusing).

**Output**: `research.md` with all decisions and rejected alternatives recorded.

## Phase 1: Design & Contracts

**Prerequisites**: `research.md` complete ✅

Artifacts produced by this phase (committed to `specs/003-wallet-import/`):

1. **`data-model.md`** — concrete shape of `WalletOptions` (with `LabelMapping` semantics and case-folding rule), `IWalletApiClient` (the 4-method surface), `WalletApiException` (the 4 `Kind` variants), `DuplicateMatcher` (the pure pairing function with its determinism contract), `LabelMappingResolver` (the `RawName → labelId[]` lookup), `WalletImportService` (the orchestrator), plus the new request/response DTOs for the two endpoints. Field-by-field with semantics, invariants, and which spec FR each piece backs.

2. **`contracts/openapi.yaml`** — copy of `002-multi-bank-support/contracts/openapi.yaml` extended **additively**:
   - Existing `POST /api/statements/extract` and all existing schemas remain **unchanged** (preserves the `002` byte-identical guarantee).
   - Four new paths: `GET /api/wallet/accounts`, `GET /api/wallet/categories`, `POST /api/wallet/import/compare`, `POST /api/wallet/import/submit`.
   - New schemas: `WalletAccount`, `WalletCategory`, `CompareResponse`, `PdfRow`, `WalletRow`, `Pairing`, `StatementWindow`, `SubmitRequest`, `SubmitRequestRow`, `SubmitResponse`, `SubmitOutcome`, `WalletErrorResponse`, `WalletErrorCode` (enum: `WALLET_NOT_CONFIGURED`, `WALLET_CREDENTIALS_INVALID`, `WALLET_UNAVAILABLE`, `WALLET_REJECTED`).
   - **No changes to** `ErrorCodes` or `ExtractionErrorResponse` (FR-006 preserves the existing PDF error envelope).

3. **`quickstart.md`** — step-by-step recipe for (a) configuring the Wallet integration (set `Wallet:BaseUrl`, `Wallet:Jwt`, `Wallet:LabelMapping` in `appsettings.json` or env vars; explain the env-var underscore-doubling convention), (b) running the backend + frontend locally and verifying the existing extract page is untouched, (c) running the full Compare → Submit flow end-to-end against a real Wallet account using the bundled BAC sample PDF, (d) running the test suite (including the fake-HttpMessageHandler tests so the dev does not need a real Wallet token to validate the implementation), and (e) updating the `Wallet:LabelMapping` for new cardholder names.

4. **Agent context update** — replace the existing `<!-- SPECKIT START -->` block in `CLAUDE.md` to point at this plan (`specs/003-wallet-import/plan.md`) so future Claude sessions in this repo pick up the new structural rules without re-reading the `002-multi-bank-support/plan.md`. The `002` plan remains authoritative for parts this plan does not change (the multi-bank seam, the extraction endpoint contract).

### Post-Design Constitution Re-check

Constitution remains unratified ⇒ no gates to re-evaluate. The five recommended principles from `001`/`002` are honored by the design: the new flow is deterministic (FR-025, encoded in `DuplicateMatcher` and `CompareResponse` ordering); reuses `CardStatement.Core` (`IBankResolver` invoked inside `WalletImportService`); is stateless (the frontend resends rows on Submit, no session); has honest errors (separate `WalletErrorResponse` envelope, four codes mapping cleanly to operator action); and introduces one narrow seam per external system (`IWalletApiClient`).

## Complexity Tracking

No constitution violations to justify. The plan deliberately *avoids* several common over-engineering traps and they are listed here so a reviewer can confirm they were considered and rejected on purpose:

| Avoided complexity | Why rejected for this feature |
|---|---|
| A new `CardStatement.Wallet` project (separate csproj) | Adds a build boundary that protects nothing — `Api` is the only consumer, and the Wallet folder is small. Re-evaluate only if a second host (e.g. a job runner) needs Wallet too. |
| Generated Wallet OpenAPI client (NSwag/Kiota) | The Wallet OpenAPI is ~7k lines; we use 4 endpoints. Hand-written `IWalletApiClient` is smaller end-to-end (interface + 4 methods) than tooling overhead, and tests can fake it with a single `HttpMessageHandler`. |
| Session-state caching of extracted PDFs between Compare and Submit | The spec is explicit about statelessness; the frontend already has the rows in memory and resends them on Submit. Caching would invite TTL bugs and surprise the user when their cache is gone. |
| A generic "external integration" framework | YAGNI. One integration justifies one folder; a second integration (if it ever comes) can extract the pattern then. |
| Refresh-token / OAuth handling | Explicit non-goal in the spec (Out of Scope). Operator-managed JWT only. |
| Per-row Wallet retries on transient 5xx | Out of scope; the spec says the frontend surfaces failures and the user retries. Quiet retries can mask real upstream issues and make the per-row outcome list confusing. |
| Persisting comparison/submit history | Out of scope. No storage layer to add. |
| Server-side category-suggestion (LLM or heuristic) | Out of scope; categories are picked manually per row in this iteration. |
| Hot-reload of `WalletOptions` via `IOptionsMonitor` | The token and label map only change when the operator restarts the backend; snapshotting via `IOptions<>` is sufficient and simpler. Reading the value per request keeps reload trivial: restart picks it up. |
| Cancellation tokens throughout the pipeline | Single-PDF imports complete in seconds on a developer machine; adding `CancellationToken` to every method is noise. Add when a cancellation use case exists (e.g. user-clicks-cancel). |
| Frontend state library (Redux / Zustand) | The page state is local to one component; `useReducer` mirrors how the existing page already works. A library is overkill. |
| Server-Sent Events / WebSockets for progress | Submit completes in seconds for ≤50 rows; a single round trip per chunk is fine. |
