# Phase 0 Research: PDF Extract & Display (Web MVP)

**Feature**: `001-pdf-extract-web`
**Date**: 2026-05-28
**Status**: Complete — no unresolved `NEEDS CLARIFICATION` markers carried in.

The spec arrived without any `[NEEDS CLARIFICATION]` markers (the `/speckit-specify` pass used reasonable defaults and recorded them in **Assumptions**). The research questions below therefore cover **technology choices** and **integration patterns** that the spec deferred to planning, not gaps in the requirements.

---

## R1. Backend language / framework

**Decision**: ASP.NET Core Minimal API on .NET 10 (`net10.0`, matching `global.json` SDK `10.0.201`).

**Rationale**:
- The deterministic parser already exists in `src/CardStatement.Core` as a C# library (`net10.0`, depends on `UglyToad.PdfPig 1.7.0-custom-5`) and is the *source of truth* for SC-002 (row-for-row parity).
- Reusing it as a project reference from a sibling `CardStatement.Api` project is a one-line `ProjectReference` and zero new abstractions. Logic moves from `Pipeline.RunAsync(pdfPath)` in `CardStatement.App` into an HTTP handler that calls `IPdfExtractor → IStatementParser → IReconciler` directly (the three Core services that don't depend on the LLM / Category API / Labels API surface).
- Minimal API gives the smallest possible surface — one `MapPost` call, no controllers, no MVC pipeline.
- TreatWarningsAsErrors + Nullable + EnforceCodeStyleInBuild are already enforced via `Directory.Build.props`; the new project inherits them automatically.

**Alternatives considered & rejected**:
- **Node/Express + `pdf-parse` or `pdfjs-dist`** — would require reimplementing the BAC grammar, coordinate-band table locator, and Dec→Jan rollover logic in TypeScript. Reintroduces every SC-002 risk and discards the existing `tests/CardStatement.Tests` coverage. Rejected.
- **Python/FastAPI + `pdfplumber`** — same parity risk. Rejected.
- **Keep parsing in C# but expose via Native AOT / single-file binary called from Node** — adds a brittle process-boundary for a problem an in-process call solves trivially. Rejected.
- **ASP.NET Core MVC controllers** — strictly more ceremony than Minimal API for a single endpoint. Rejected.

---

## R2. HTTP upload shape

**Decision**: `POST /api/statements/extract`, content type `multipart/form-data`, single form field `file` (the PDF). Response is `application/json`.

**Rationale**:
- `multipart/form-data` is the native browser upload mechanism — `<input type="file">` + `FormData` + `fetch`. No base64 inflation (~33% size overhead avoided).
- ASP.NET Core has first-class `IFormFile` support in Minimal API (`endpoint.DisableAntiforgery()` is required because no MVC anti-forgery token is present; that is acceptable for a localhost dev-only deploy and is recorded in `quickstart.md`).
- One-shot synchronous response (no SSE / WebSockets / polling) because the parse is bounded at < 3 s for typical inputs and the UX is "click → spinner → table".

**Alternatives considered & rejected**:
- **JSON body with base64-encoded PDF** — wastes bandwidth and memory; no benefit for a binary file. Rejected.
- **Pre-signed direct-to-storage upload** — assumes server-side storage, which the spec forbids (FR-004 / stateless). Rejected.
- **Streaming response (SSE) with row-by-row events** — overkill for a one-shot parse; complicates the frontend state machine. Rejected.

---

## R3. Response & error envelope

**Decision**:
- **Success (HTTP 200)**: flat `ExtractedStatementResponse` JSON object (no `{ data: ... }` wrapping).
- **Error (HTTP 4xx/5xx)**: `ExtractionErrorResponse` with shape `{ "error": { "code": "<CODE>", "message": "<human readable>" } }`.

Error codes and their HTTP statuses:

| Code | HTTP | When |
|---|---|---|
| `INVALID_FILE_TYPE` | 400 | Uploaded file is not a PDF (content-type check + magic-byte sniff `%PDF-`) |
| `EMPTY_FILE` | 400 | `IFormFile.Length == 0` |
| `FILE_TOO_LARGE` | 413 | File exceeds 25 MB cap |
| `PASSWORD_PROTECTED` | 422 | PdfPig throws on encrypted PDF |
| `NO_TEXT_EXTRACTABLE` | 422 | PdfPig opens the document but extracts zero words (scanned/image-only PDF) |
| `UNRECOGNIZED_LAYOUT` | 422 | Parser ran but found no cardholder section header AND no transaction rows |
| `PARSE_FAILED` | 500 | Catch-all for unexpected exceptions; details NOT included in the response (only logged server-side) |

**Rationale**:
- One success shape, one error shape, easy to type on the frontend with a discriminated union (`status` derived from `response.ok`).
- Error codes are stable identifiers the frontend switches on; the `message` is for direct display.
- Splitting 4xx vs. 422 mirrors the standard distinction: 4xx = the client request itself is malformed (wrong file type, too big); 422 = the request is well-formed but the content can't be processed (encrypted, scanned, wrong layout).
- Maps cleanly to spec User Story 3 (four distinct error categories called out: non-PDF, scanned, unrecognized layout, password-protected).

**Alternatives considered & rejected**:
- **RFC 7807 Problem Details** — slightly heavier shape (`type`, `title`, `status`, `detail`, `instance`) for marginal benefit on an internal API with one endpoint. Rejected.
- **Always-200 with `{ ok: false, error: ... }`** — hides errors from browser devtools' network tab and from any future CDN/proxy. Rejected.

---

## R4. Forward compatibility for enrichment (categoryId / labelId)

**Decision**: include `categoryId`, `categoryName`, `labelId`, `labelName` on every `transaction` in the response, **always present, always `null`** in this iteration. Add a `reconciliationStatus` field at section and statement level using the existing `Core.Models.Enums.ReconciliationStatus` enum (`Ok`, `NeedsReview`, etc.) serialized as a lowercase string.

**Rationale**:
- The spec **Assumptions** require that the response shape "leave room for them later without forcing a breaking change when they are added". Including the fields as `null` now means the eventual enrichment spec only changes *values*, not the schema. TypeScript types on the frontend will already type them as `string | null` so no consumer code changes either.
- Omitting the fields and adding them later is a breaking change for strict JSON schemas (additive in practice for JS, breaking for typed clients in Go/Rust/etc.).

**Alternatives considered & rejected**:
- **Omit the fields entirely** — works for a JS-only consumer but bakes a future breaking change into the contract. Rejected.
- **Wrap in `enrichment: { categoryId, ... }` object that's `null` for now** — adds nesting for no payoff. Rejected.

---

## R5. CORS

**Decision**: Backend allows `http://localhost:5173` (Vite dev default) and the configured production origin (none for this iteration). Configured in `appsettings.json` under `Cors:AllowedOrigins` so the value isn't hard-coded.

**Rationale**:
- Vite dev server runs on 5173; the API runs on a different port (5080), so same-origin doesn't apply during development.
- Hard-coding any origin would force a code change to deploy elsewhere; reading from config preserves the "no code change for environment" rule even in this small project.

**Alternatives considered & rejected**:
- **Run Vite behind the .NET dev server (`UseSpa`)** — adds coupling between the two stacks and complicates `pnpm run dev` ergonomics. Rejected.
- **`AllowAnyOrigin()`** — fine for localhost, sloppy as a habit and easy to forget when the project grows. Rejected.

---

## R6. Frontend framework / tooling

**Decision**: Vite 5+ scaffold (`pnpm create vite@latest frontend --template react-ts`), React 19, TypeScript 5, pnpm as the package manager. No router, no state library, no UI framework, no fetch wrapper.

**Rationale**:
- The user explicitly requested Vite + pnpm + React.
- The MVP is one screen with one async flow; every additional dependency would be carrying weight it doesn't earn.
- `useReducer` neatly models the upload state machine (`idle | uploading | success | error`), and React Testing Library can drive it in tests without any extra plumbing.

**Alternatives considered & rejected**:
- **Next.js / Remix** — SSR/routing overhead for a single-page tool. Rejected.
- **TanStack Query** — one endpoint, one call per user action. `fetch` + `useReducer` is shorter and clearer. Adopt later if endpoints multiply.
- **Tailwind / Mantine / shadcn-ui** — one screen; plain CSS keeps the diff small. The table can ship with ~50 lines of CSS.
- **`pdfjs-dist` in the browser for client-side parsing** — would defeat the purpose of having an API and re-introduces the parity risk. Rejected.

---

## R7. Streaming vs. buffering on the backend

**Decision**: Buffer the uploaded `IFormFile` to a request-scoped `MemoryStream`, then write it to a temp file (because `IPdfExtractor.Extract` currently takes a path string). Delete the temp file in a `finally` block.

**Rationale**:
- `IPdfExtractor.Extract(string pdfPath)` in `CardStatement.Core` takes a file path today (see `src/CardStatement.App/Pipeline.cs:53`). A future refactor to add an overload accepting `Stream` would be cleaner, but is **out of scope** for this iteration — adding the overload risks touching shared parser code right before the API depends on it. The temp-file shim is 5 lines and fully cleaned up.
- Using `Path.GetTempFileName()` puts the file in the OS temp dir; the `finally` guarantees deletion even on parse failure. FR-004 is satisfied — bytes are not persisted past the request.

**Follow-up (NOT in this spec)**: add `IPdfExtractor.Extract(Stream)` overload in a later refactor to remove the temp-file dance.

**Alternatives considered & rejected**:
- **Refactor `IPdfExtractor` now to accept a stream** — couples this MVP to a Core change; touches code paths used by the existing console PoC and tests. Rejected for risk-aversion; reconsider later.
- **Stream directly from `IFormFile.OpenReadStream()` into PdfPig via the existing path-based API** — impossible without the new overload.

---

## R8. File-size & request-body limits

**Decision**: Enforce a 25 MB upload cap at three places:
1. Frontend: reject before submit (`File.size > 25 * 1024 * 1024`).
2. ASP.NET Core Kestrel `MaxRequestBodySize` set to 25 MB.
3. The Minimal API endpoint itself re-checks `IFormFile.Length` and returns `FILE_TOO_LARGE` (in case a future proxy bypasses Kestrel limits).

**Rationale**:
- Spec **Assumptions** state "~25 MB" is acceptable. Sample statement is < 5 MB. The cap is generous but bounded.
- Three layers because each catches a different bypass: frontend gives instant feedback without a round-trip; Kestrel guards the process; the endpoint guards against framework-config drift.

**Alternatives considered & rejected**:
- **No cap** — opens a trivial DoS vector even on localhost. Rejected.
- **Smaller cap (5 MB)** — fragile if a future statement is larger; 25 MB has no realistic downside on a developer laptop. Rejected.

---

## R9. Logging & PII

**Decision**: `Microsoft.Extensions.Logging` at default `Information` level. Log per request: filename (sanitized — basename only, no path), file size in bytes, page count, transaction row count, per-section reconciliation status. Do **NOT** log: raw PDF bytes, full transaction descriptions, amounts, dates, masked-account number, cardholder names. At `Debug` level, log the truncated first 32 chars of any description that fails to parse, but only as `Debug` (default off).

**Rationale**:
- Spec **Assumptions** require: "the backend MUST NOT log raw PDF bytes or full transaction descriptions at default log levels."
- Filename + size + page/row counts are enough to debug "did the upload arrive and did the parser see something" without leaking personal financial data.
- Reconciliation status per section is operationally useful and contains no PII.

**Alternatives considered & rejected**:
- **Log nothing** — makes operational debugging impossible. Rejected.
- **Log everything at Information** — violates the spec's privacy assumption. Rejected.

---

## R10. Test strategy

**Decision**:
- **Backend** — `tests/CardStatement.Api.Tests` (xUnit, matches existing `tests/CardStatement.Tests`). Uses `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>` to spin up the API in-process and exercise the endpoint with `HttpClient`. Cases:
  1. Happy path: upload `/samples/final5140_45178439_316493_0.pdf`, assert 200, assert response matches the ground truth in `result.json` row-for-row (satisfies SC-002).
  2. Empty file → 400 `EMPTY_FILE`.
  3. Non-PDF (e.g. a text file with `.pdf` extension) → 400 `INVALID_FILE_TYPE` (caught by magic-byte sniff).
  4. Oversized file (28 MB of zeros) → 413 `FILE_TOO_LARGE`.
  5. Scanned-only PDF stub (PDF with no extractable text) → 422 `NO_TEXT_EXTRACTABLE`.
  6. Wrong-bank PDF stub → 422 `UNRECOGNIZED_LAYOUT`.
- **Frontend** — Vitest + React Testing Library + jsdom. Cases:
  1. `UploadForm` rejects non-PDF and oversized files before calling `fetch`.
  2. `App` integration test mocks `fetch` to return the canned happy-path JSON, asserts header + sections + totals render.
  3. `App` integration test mocks `fetch` to return each error code, asserts the matching message appears.

**Rationale**:
- xUnit + `WebApplicationFactory` is the same stack already used in `tests/CardStatement.Tests`, so contributors don't context-switch.
- Vitest is the Vite-native choice — zero extra config beyond `pnpm add -D vitest @testing-library/react jsdom`.
- The row-for-row test against `result.json` is the single most important assertion in the entire codebase — it locks in SC-002.

**Alternatives considered & rejected**:
- **Playwright end-to-end** — too heavy for this MVP (requires spinning both servers + a real browser). The Vitest integration test with mocked fetch covers the same surface for our purposes; add Playwright later if regression coverage demands it.
- **Snapshot tests for JSON responses** — brittle to ordering/whitespace and would hide intentional changes. Explicit asserts against `result.json` are clearer.

---

## R11. Versioning of the API

**Decision**: No `/v1/` URL prefix in this iteration. Path is `/api/statements/extract` (singular `api`, no version segment).

**Rationale**:
- Single consumer (own frontend), private network, one endpoint. Versioning ceremony costs > benefit at this stage.
- Adding `/v1/` later is a one-line route change and a deprecation window for a single caller.

**Alternatives considered & rejected**:
- **`/api/v1/statements/extract`** — premature; revisit when there's a real second consumer or a real breaking change.

---

## Open follow-ups (intentionally deferred — NOT in this spec)

1. Add `IPdfExtractor.Extract(Stream)` overload in `CardStatement.Core` to remove the temp-file shim (R7).
2. Containerize the API + frontend (Dockerfile + docker-compose) when a real deployment target appears.
3. Wire LLM categorization and Labels API resolution — handled by the **next** spec, which extends the response with non-null `categoryId`/`labelId`/`categoryName`/`labelName` values.
4. CSV/JSON download buttons in the UI — explicitly out of scope for this iteration per spec.
