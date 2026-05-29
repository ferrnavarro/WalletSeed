# Implementation Plan: PDF Extract & Display (Web MVP)

**Branch**: `001-pdf-extract-web` | **Date**: 2026-05-28 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-pdf-extract-web/spec.md`

## Summary

Expose the existing deterministic BAC Credomatic PDF parser (already proven in `src/CardStatement.Core`) over an HTTP API, and build a Vite + React + pnpm frontend that lets a user upload a statement PDF and see every extracted transaction grouped by cardholder section, with computed-vs-printed totals shown side by side. This iteration deliberately **skips** LLM categorization, Labels API resolution, persistence, and auth (those exist in the long-term `SPEC.md` and ship in a later spec). The backend reuses `IPdfExtractor` → `IStatementParser` → `IReconciler` from `CardStatement.Core` directly; no parser code is rewritten.

## Technical Context

**Language/Version**: Backend C# 13 / .NET 10 (matches existing `global.json` SDK `10.0.201` and `Directory.Build.props` target `net10.0`). Frontend TypeScript 5.x.
**Primary Dependencies**:
- Backend: ASP.NET Core Minimal API (built-in), existing `CardStatement.Core` project reference, `UglyToad.PdfPig 1.7.0-custom-5` (transitive via Core), `Microsoft.Extensions.Logging.Abstractions` (transitive).
- Frontend: Vite 5+, React 19, TypeScript 5+, native `fetch` for upload. **No** state library, **no** UI framework, **no** data-fetching library for the MVP (`useState` + `useReducer` only).
**Storage**: None. Stateless — uploaded PDF bytes are buffered to a request-scoped stream, parsed in-memory, and discarded when the response is written (FR-004).
**Testing**: Backend uses xUnit (matches existing `tests/CardStatement.Tests`). New `tests/CardStatement.Api.Tests` project covers the HTTP contract via `WebApplicationFactory`. Frontend uses Vitest + React Testing Library + jsdom; one component test for the table, one integration test for upload happy/error paths.
**Target Platform**: Backend runs on macOS / Linux developer machine (localhost, port `5080`). Frontend dev server is Vite on `5173`. Latest evergreen desktop browsers (Chrome, Firefox, Safari, Edge).
**Project Type**: web (HTTP API + separate SPA frontend).
**Performance Goals**: End-to-end upload → table render in **< 10 s** for the bundled sample PDF on a developer laptop (SC-001). Parse-only time should stay **< 3 s** for statements up to ~10 pages (the existing console PoC already meets this; the API wrapper adds negligible overhead).
**Constraints**: Stateless backend (no DB). Upload size cap **25 MB** (FR-002, spec Assumptions). Backend MUST NOT log raw PDF bytes or full transaction descriptions at default log level (spec Assumptions). Deterministic — same input bytes ⇒ byte-identical output (FR-011, SC-007). No external network calls (no Category/Labels API, no LLM) — fully offline.
**Scale/Scope**: Single-user / small group of trusted collaborators on localhost or private network. Concurrency target: handle 5 concurrent uploads without contention (each request is isolated; the only shared state is the `IPdfExtractor` singleton which is stateless internally).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The project constitution at `.specify/memory/constitution.md` is **unratified** — it is the unmodified Speckit template with placeholder principles (`[PRINCIPLE_1_NAME]`, etc.) and no real rules. There are therefore no concrete gates to evaluate.

**Status**: PASS (vacuously — no ratified principles to violate).

**Action recommended (out of scope for this plan)**: ratify a constitution before this codebase grows. The most load-bearing principles to capture, based on `SPEC.md` and this MVP, would be:
1. **Deterministic extraction is the source of truth**: no LLM in the parse path; PdfPig coordinates only.
2. **Reuse `CardStatement.Core`**: new surfaces (API, UI) wrap Core, never reimplement it.
3. **Stateless services**: no DB until a feature genuinely needs durable state.
4. **Honest errors over silent empty results**: every failure path returns a structured, distinguishable code.

These are *recommendations*, not enforced gates for this plan.

## Project Structure

### Documentation (this feature)

```text
specs/001-pdf-extract-web/
├── plan.md              # This file (/speckit-plan command output)
├── spec.md              # /speckit-specify output (already exists)
├── research.md          # Phase 0 output (this command)
├── data-model.md        # Phase 1 output (this command)
├── quickstart.md        # Phase 1 output (this command)
├── contracts/
│   └── openapi.yaml     # Phase 1 output (this command)
├── checklists/
│   └── requirements.md  # /speckit-specify output (already exists)
└── tasks.md             # Phase 2 output (/speckit-tasks command — NOT created here)
```

### Source Code (repository root)

```text
src/
├── CardStatement.Core/           # EXISTING — parser, models, reconciler. Untouched.
├── CardStatement.App/            # EXISTING — console PoC. Untouched.
└── CardStatement.Api/            # NEW — ASP.NET Core Minimal API
    ├── CardStatement.Api.csproj  #   refs CardStatement.Core
    ├── Program.cs                #   minimal API bootstrap, DI registration, CORS, endpoint mapping
    ├── Endpoints/
    │   └── ExtractEndpoint.cs    #   POST /api/statements/extract
    ├── Contracts/                #   response/error DTOs (camelCase JSON)
    │   ├── ExtractedStatementResponse.cs
    │   ├── CardholderSectionDto.cs
    │   ├── TransactionDto.cs
    │   ├── TotalsPairDto.cs
    │   └── ExtractionErrorResponse.cs
    ├── Mapping/
    │   └── StatementMapper.cs    #   Core.Models.Statement → ExtractedStatementResponse
    ├── ErrorHandling/
    │   └── ExtractionExceptionFilter.cs  # maps known parse failures → structured errors
    └── appsettings.json          #   CORS origins, upload size cap

tests/
├── CardStatement.Tests/                  # EXISTING — unit/integration/E2E for Core. Untouched.
└── CardStatement.Api.Tests/              # NEW — xUnit + WebApplicationFactory
    ├── CardStatement.Api.Tests.csproj
    ├── ExtractEndpointTests.cs           # happy path (sample PDF), error paths
    └── Fixtures/
        └── (uses /samples/final5140_45178439_316493_0.pdf)

frontend/                                  # NEW — Vite + React + TS, managed by pnpm
├── package.json
├── pnpm-lock.yaml
├── pnpm-workspace.yaml                   # optional; lets pnpm scope to ./frontend
├── tsconfig.json
├── vite.config.ts
├── index.html
├── public/
├── src/
│   ├── main.tsx
│   ├── App.tsx                           # single page: upload form + result view
│   ├── api/
│   │   └── statementsClient.ts           # fetch wrapper, typed against /contracts/openapi.yaml
│   ├── components/
│   │   ├── UploadForm.tsx                # file input, size/type guard, submit
│   │   ├── StatementHeader.tsx           # card type, masked account, period
│   │   ├── CardholderSection.tsx         # section header + transactions table + totals
│   │   ├── TransactionRow.tsx
│   │   ├── TotalsPair.tsx                # computed vs printed, mismatch highlight
│   │   └── ErrorBanner.tsx
│   ├── types/
│   │   └── api.ts                        # TS types mirroring the OpenAPI contract
│   └── styles.css                        # plain CSS, no framework
└── tests/
    ├── UploadForm.test.tsx               # rejects non-PDF, oversized, empty
    └── App.integration.test.tsx          # mocks fetch; full happy path + one error path

CreditStatementParser.slnx                # UPDATED — register CardStatement.Api + Api.Tests projects
```

**Structure Decision**: **Option 2 (web application)** — backend API + separate frontend. The `src/` tree keeps its existing two .NET projects and adds a third (`CardStatement.Api`); the frontend lives in a new top-level `frontend/` directory (kept outside `src/` because `src/` is conceptually the .NET solution root and the frontend has its own tooling/lockfile). The solution file `CreditStatementParser.slnx` gets two new entries (`CardStatement.Api`, `CardStatement.Api.Tests`).

## Phase 0: Outline & Research

See [research.md](./research.md) for the full write-up. Decisions resolved:

1. **Backend language/framework** — ASP.NET Core Minimal API on .NET 10. *Rationale*: parser is already C#; rewriting in another language to satisfy a generic "any language" preference would re-introduce the determinism / parity risk the spec explicitly guards against (SC-002, FR-011).
2. **API shape** — single endpoint `POST /api/statements/extract` taking `multipart/form-data` with a `file` part. *Rationale*: simplest correct upload model for a browser; no JSON-base64 inflation.
3. **Response envelope** — flat `ExtractedStatementResponse` (200) OR `ExtractionErrorResponse` (4xx/5xx). No wrapping envelope. Forward-compatible: enrichment fields are present as `null` on transactions, never absent.
4. **CORS** — dev only: allow `http://localhost:5173`. Prod: disabled (no deploy in this iteration).
5. **Frontend stack** — Vite + React 19 + TypeScript, pnpm. No router (single page), no UI framework, no fetch library — native `fetch` is sufficient. *Rationale*: spec is one screen; every extra dep is unjustified for this MVP.
6. **Frontend state** — `useReducer` for the upload state machine (`idle | uploading | success | error`); no global store.
7. **Test stacks** — backend xUnit + `WebApplicationFactory` (matches existing). Frontend Vitest + React Testing Library + jsdom.
8. **Error taxonomy** — five distinguishable codes (`INVALID_FILE_TYPE`, `FILE_TOO_LARGE`, `PASSWORD_PROTECTED`, `NO_TEXT_EXTRACTABLE`, `UNRECOGNIZED_LAYOUT`, plus a generic `PARSE_FAILED` fallback). Mapped to HTTP 400 / 413 / 422 / 500 per `research.md`.
9. **Logging** — `Microsoft.Extensions.Logging` at default `Information` level. PDF bytes and full transaction descriptions are NEVER logged; only filename, size, page count, row count, and per-section reconciliation status.

**Output**: `research.md` with all decisions and rejected alternatives recorded.

## Phase 1: Design & Contracts

**Prerequisites**: `research.md` complete ✅

Artifacts produced by this phase (committed to `specs/001-pdf-extract-web/`):

1. **`data-model.md`** — concrete shape of the `ExtractedStatementResponse` and `ExtractionErrorResponse`, with field-by-field mapping from `CardStatement.Core.Models.Statement` → DTO. Includes the forward-compatible enrichment fields (`categoryId`/`labelId` nullable, default `null`).

2. **`contracts/openapi.yaml`** — OpenAPI 3.1 spec for the single endpoint `POST /api/statements/extract`. Used as the source of truth for both backend response shape and frontend TypeScript types (`frontend/src/types/api.ts` is derived by hand or via `openapi-typescript` if added later — not required for MVP).

3. **`quickstart.md`** — how to run the new API and frontend locally end-to-end. Includes the exact commands to verify SC-001 (upload sample PDF and see rows in < 10 s) and SC-002 (compare against console PoC's `result.json`).

4. **Agent context update** — `CLAUDE.md` `<!-- SPECKIT START -->` block updated to reference this plan (`specs/001-pdf-extract-web/plan.md`).

### Post-Design Constitution Re-check

Constitution remains unratified ⇒ no gates to re-evaluate. Recommendations from the pre-check still stand.

## Complexity Tracking

No constitution violations to justify (no ratified constitution). The plan deliberately *avoids* the following common over-engineering traps and they are listed here so a reviewer can confirm they were considered and rejected on purpose:

| Avoided complexity | Why rejected for this MVP |
|---|---|
| Adding a database / upload history | Spec explicitly excludes persistence; would block stateless guarantee in FR-004. |
| State library (Zustand/Redux) on the frontend | One page, one async flow — `useReducer` is sufficient. |
| Data-fetching library (TanStack Query) | One endpoint, one call per user action — `fetch` + `useReducer` is sufficient. |
| UI framework (Tailwind, MUI, Mantine) | One screen; plain CSS keeps the diff and build small. Easy to add later if the surface grows. |
| Rewriting the parser in TypeScript / Node | Would re-introduce the parity risk that SC-002 exists to guard against. Reusing `CardStatement.Core` is strictly safer. |
| Auth / multi-user | Out of scope per spec; introducing it here would be premature. |
| OpenAPI codegen in the frontend build | One endpoint; a hand-written `types/api.ts` mirroring the contract is cheaper than wiring a codegen step. Revisit if endpoints grow past ~3. |
