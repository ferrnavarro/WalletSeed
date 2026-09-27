# Implementation Plan: Multi-Bank Backend Support

**Branch**: `002-multi-bank-support` | **Date**: 2026-05-29 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/002-multi-bank-support/spec.md`

## Summary

Refactor the existing `CardStatement.Core` + `CardStatement.Api` codebase so that "a bank" becomes a first-class, registered seam. Today every step from word-classification to section-parsing assumes BAC Credomatic (its BIN `459378`, its Spanish column headers `CONCEPTO/CARGOS/ABONOS`, its `SUBTOTAL.:` markers). After this plan, the only BAC-aware code lives behind one narrow interface (`IBankProvider`) inside a self-contained `Banks/Bac/` folder; the HTTP endpoint, the shared statement model, the reconciler, and the PDF text-extraction layer are all bank-agnostic. A new bank is added by dropping in one new `BankProvider` implementation, registering it once in `Program.cs`, and shipping its tests — zero edits to BAC, the endpoint, the model, the reconciler, or the response DTOs (SC-003). The behavior of the existing BAC flow is preserved byte-for-byte modulo one additive `bank` field on the response (SC-002 / FR-009).

The shape that makes this cheap: one interface (`IBankProvider`) bundling **identity + detection + parse** per bank, one detection result type (`BankDetection`), one registry with deterministic tie-breaking, one DI-friendly `BankResolver` that the endpoint depends on instead of `IStatementParser`. `IStatementParser` becomes a per-bank implementation detail, no longer a service.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (matches existing `global.json` SDK `10.0.201` and `Directory.Build.props` target `net10.0`). No language or runtime change.
**Primary Dependencies**: No new dependencies. Existing `UglyToad.PdfPig 1.7.0-custom-5` (transitive via `CardStatement.Core`), `Microsoft.Extensions.Logging.Abstractions`, ASP.NET Core Minimal API (built-in). The refactor is structural; it does not introduce a plugin loader, a DI container plugin, or any reflection.
**Storage**: None. Stateless backend; carry forward from `001-pdf-extract-web`.
**Testing**: xUnit (existing `tests/CardStatement.Tests`, `tests/CardStatement.Api.Tests`). New per-bank test class lives next to its bank (`tests/CardStatement.Tests/Banks/Bac/` for BAC; a future bank gets `tests/CardStatement.Tests/Banks/<NewBank>/`). API-level tests covering the resolver + endpoint contract live in `tests/CardStatement.Api.Tests`.
**Target Platform**: Backend on macOS / Linux developer machines (localhost, port `5080`). Unchanged from `001-pdf-extract-web`.
**Project Type**: web service (HTTP API) — this spec touches only the backend; the existing `frontend/` directory is not modified (FR-009, SC-002).
**Performance Goals**: Adding a second registered bank costs ≤ **+10%** end-to-end extraction latency for the existing BAC sample (SC-005). Detection runs against already-extracted words in memory; PDF text extraction happens at most once per request (FR-007).
**Constraints**: Stateless, deterministic (FR-012). Bank registry is fixed at startup; zero registered banks fails loudly (FR-015). No external network calls. Logging-privacy constraints from `001-pdf-extract-web` (no raw PDF bytes, no full transaction descriptions at default level) carry forward (FR-017). Response shape changes MUST be additive only — existing fields keep their names, order, and types (FR-009, SC-002).
**Scale/Scope**: Same scale as `001-pdf-extract-web` (single-user / small trusted-collaborator group on localhost). The registry is expected to grow to ~5–10 banks over time, not hundreds; the detection loop is intentionally O(banks) per request with each bank's detector running over already-extracted words.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The project constitution at `.specify/memory/constitution.md` is **unratified** (unmodified Speckit template with placeholder principles). No concrete gates to evaluate.

**Status**: PASS (vacuously — no ratified principles to violate).

The `001-pdf-extract-web` plan listed four recommended principles (deterministic extraction, reuse `CardStatement.Core`, stateless services, honest errors). This plan honors all four and adds a fifth that this refactor crystallizes:

5. **One narrow seam per variation point.** When the system needs to vary along an axis (here: which bank issued the PDF), introduce exactly one interface that bundles all bank-varying behavior, register implementations explicitly in `Program.cs`, and forbid bank-specific code from leaking into shared layers (models, endpoints, reconciler).

These remain *recommendations*. No constitution gates fail.

## Project Structure

### Documentation (this feature)

```text
specs/002-multi-bank-support/
├── plan.md              # This file (/speckit-plan command output)
├── spec.md              # /speckit-specify output (already exists)
├── research.md          # Phase 0 output (this command)
├── data-model.md        # Phase 1 output (this command)
├── quickstart.md        # Phase 1 output (this command)
├── contracts/
│   └── openapi.yaml     # Phase 1 output (this command) — additive diff vs 001
├── checklists/
│   └── requirements.md  # /speckit-specify output (already exists)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── CardStatement.Core/                              # REFACTORED INTERNALLY — public surface preserved
│   ├── CardStatement.Core.csproj
│   ├── Abstractions/
│   │   ├── IPdfExtractor.cs                         # UNCHANGED — bank-agnostic
│   │   ├── IReconciler.cs                           # UNCHANGED — bank-agnostic
│   │   ├── IStatementParser.cs                      # UNCHANGED interface; now an internal-use type per bank
│   │   ├── IBankProvider.cs                         # NEW — the single seam (identity + detect + parse)
│   │   ├── IBankRegistry.cs                         # NEW — read-only view of registered banks
│   │   └── IBankResolver.cs                         # NEW — orchestrates detect-then-parse; the endpoint's dependency
│   ├── Banks/
│   │   ├── BankInfo.cs                              # NEW — { Id, DisplayName } value type
│   │   ├── BankDetection.cs                         # NEW — { Matched, Confidence, Reason } per-bank detection result
│   │   ├── BankRegistry.cs                          # NEW — immutable collection, validates non-empty at startup
│   │   ├── BankResolver.cs                          # NEW — iterates registry, applies tie-break, parses
│   │   ├── BankResolutionResult.cs                  # NEW — internal { Bank, Statement } returned by resolver
│   │   ├── Exceptions/
│   │   │   ├── NoBankMatchedException.cs            # NEW — translates to UNRECOGNIZED_LAYOUT in API layer
│   │   │   └── EmptyBankRegistryException.cs        # NEW — thrown by registry ctor; surfaces startup failure
│   │   └── Bac/                                     # NEW FOLDER — all BAC-specific code moves here
│   │       ├── BacBankProvider.cs                   # NEW — implements IBankProvider; wires the BAC pieces below
│   │       ├── BacDetector.cs                       # NEW — encapsulates "does this PDF look like BAC?"
│   │       ├── BacStatementParser.cs                # MOVED from Parsing/StatementParser.cs (renamed)
│   │       ├── BacRowClassifier.cs                  # MOVED from Parsing/RowClassifier.cs (renamed)
│   │       ├── BacMetadataExtractor.cs              # MOVED from Parsing/StatementMetadataExtractor.cs (renamed)
│   │       ├── BacTransactionRowParser.cs           # MOVED from Parsing/TransactionRowParser.cs (renamed)
│   │       ├── BacTransactionTableLocator.cs        # MOVED from Pdf/TransactionTableLocator.cs (renamed)
│   │       ├── BacTransactionDateResolver.cs        # MOVED from Parsing/TransactionDateResolver.cs (renamed)
│   │       ├── BacRowBuilder.cs                     # MOVED from Pdf/RowBuilder.cs (renamed)
│   │       ├── BacParsingOptions.cs                 # MOVED from Pdf/ParsingOptions.cs (renamed)
│   │       ├── BacAmountParser.cs                   # MOVED from Parsing/AmountParser.cs (renamed)
│   │       └── BacSpanishMonths.cs                  # MOVED from Parsing/SpanishMonths.cs (renamed)
│   ├── Models/                                      # UNCHANGED — Statement, Transaction, CardholderSection are bank-agnostic
│   ├── Pdf/
│   │   ├── PdfPigExtractor.cs                       # UNCHANGED — bank-agnostic word extraction
│   │   ├── TableLayout.cs                           # UNCHANGED — generic structure; future banks may reuse
│   │   └── (RowBuilder / TransactionTableLocator / ParsingOptions moved to Banks/Bac/)
│   ├── Parsing/                                     # DELETED — every file was BAC-specific; all moved to Banks/Bac/
│   ├── Reconciliation/Reconciler.cs                 # UNCHANGED — bank-agnostic
│   ├── Categorization/ Labels/ Result/ Apis/        # UNCHANGED — out-of-scope subsystems
│   └── Registration/
│       └── CoreServiceCollectionExtensions.cs       # NEW — AddCardStatementCore() registers PDF extractor, reconciler, registry, resolver; deliberately does NOT register any bank
│
├── CardStatement.Api/
│   ├── Program.cs                                   # EDITED — replaces AddSingleton<IStatementParser>() with AddCardStatementCore() + AddBacBank()
│   ├── Endpoints/ExtractEndpoint.cs                 # EDITED — depends on IBankResolver instead of IStatementParser; surfaces resolver exceptions
│   ├── ErrorHandling/ExtractionExceptions.cs        # UNCHANGED (UnrecognizedLayoutException still used)
│   ├── ErrorHandling/ExtractionFailureMapper.cs     # EDITED — adds NoBankMatchedException → UNRECOGNIZED_LAYOUT mapping; generalizes message
│   ├── Mapping/StatementMapper.cs                   # EDITED — accepts the resolved BankInfo and emits the additive `bank` response field
│   ├── Contracts/ExtractedStatementResponse.cs      # EDITED — adds `BankInfoDto Bank` as the LAST field (additive)
│   └── Contracts/BankInfoDto.cs                     # NEW — { Id, DisplayName } DTO
│
└── CardStatement.App/                               # EDITED — register BAC the same way Program.cs does; no other change
    └── (DI bootstrap reuses AddCardStatementCore() + AddBacBank())

tests/
├── CardStatement.Tests/                             # EXTENDED
│   ├── Banks/
│   │   └── Bac/
│   │       ├── BacBankProviderTests.cs              # NEW — detect + parse for the sample PDF
│   │       ├── BacDetectorTests.cs                  # NEW — positive (sample) + negative (synthetic blank-PDF words) cases
│   │       └── (existing BAC parser tests move/rename here, unchanged in behavior)
│   ├── BankRegistryTests.cs                         # NEW — empty registry throws; populated registry exposes items in registration order
│   ├── BankResolverTests.cs                         # NEW — single-match / no-match / ambiguous-match / detector-throws / parser-throws
│   └── (existing reconciler / model / E2E tests UNCHANGED)
│
└── CardStatement.Api.Tests/                         # EXTENDED
    ├── ExtractEndpointTests.cs                      # EDITED — adds assertion that response includes the `bank` field with id=`bac` for the BAC sample; existing assertions UNCHANGED
    ├── MultiBankRoutingTests.cs                     # NEW — registers BAC + an in-test stub bank; asserts BAC PDF routes to BAC, stub PDF routes to stub, unknown PDF returns UNRECOGNIZED_LAYOUT
    ├── EmptyRegistryStartupTests.cs                 # NEW — asserts that a WebApplicationFactory wired without any AddXxxBank() fails at startup (SC-007)
    └── Fixtures/
        └── StubBankProvider.cs                      # NEW — test-only IBankProvider that recognizes PDFs containing a marker word and emits a hard-coded Statement
```

**Structure Decision**: Extend the existing **web-service** layout from `001-pdf-extract-web`. The frontend tree is untouched (FR-009). All new code lives inside the two existing backend projects plus their test projects — no new csproj files are created. The single most important structural decision is the new `src/CardStatement.Core/Banks/` folder, which contains both the bank-agnostic infrastructure (`BankRegistry`, `BankResolver`, `IBankProvider`) **and** each per-bank subfolder (`Banks/Bac/`, future `Banks/<NewBank>/`). Co-locating the BAC implementation in `Banks/Bac/` makes the "one folder = one bank" mental model literal, and the `IBankProvider` interface owned in `Abstractions/` makes the contract for adding a bank discoverable at a glance.

## Phase 0: Outline & Research

See [research.md](./research.md) for the full write-up. Decisions resolved:

1. **The seam shape: one interface bundling identity + detection + parse, vs. three separate registries.** Choice: one interface, `IBankProvider`. Rationale: a bank that can detect but not parse (or vice versa) is a bug, not a use case; splitting them would force every bank author to register in two or three places and would make the "add a bank by adding a file" guarantee in US2 weaker. Rejected alternative: separate `IBankDetector` and `IStatementParser<TBank>` registered independently — needlessly fragmented for zero gained flexibility.

2. **Detection input.** Choice: detection receives the already-extracted `PdfDocumentWords` (page count + positioned words). Rationale: cheapest possible signal that still lets detectors look at text, page structure, or word coordinates; satisfies FR-007 (extract-once); reuses the very thing every bank's parser needs anyway, so detectors and parsers share a cache for free. Rejected alternatives: raw PDF bytes (re-extraction per bank), filename-only (brittle, useless for re-uploads), file-magic-bytes (every bank's PDF starts with `%PDF-`).

3. **Detection result shape.** Choice: `BankDetection { bool Matched, int Confidence, string? Reason }`. Rationale: `Confidence` (a small integer score, 0–100, with documented buckets) gives the resolver a principled, deterministic tie-breaker for ambiguous matches (FR-006); `Reason` is for logs only. Rejected alternative: bare `bool` — leaves the resolver to break ties by registration order, which is fragile (re-ordering registrations in `Program.cs` would silently change which bank wins) and impossible to log usefully.

4. **Ambiguity tie-breaker.** Choice: highest `Confidence` wins; on equal confidence, lexicographic order of bank `Id`. Rationale: lexicographic id-order is deterministic across process restarts, registration order changes, and refactors (FR-012). Logged at warning level with all claimants' ids and confidences so the bank authors can fix the conflict (FR-006, FR-018). Rejected: random/round-robin (breaks determinism), throw an exception (breaks the "one buggy bank can't take down the endpoint" property — except worse, here even a *correct* but overlapping bank crashes the request).

5. **Detector-throws containment.** Choice: catch any exception from `IBankProvider.Detect(...)` in `BankResolver`, treat it as "did not match", log with the offending bank's id. Rationale: directly satisfies FR-008 and the "one buggy bank cannot take down the endpoint" edge case. Rejected: rethrow (turns a localized bug into a 500), let the framework handle it (same outcome).

6. **Parser-throws containment.** Choice: catch any exception from `IBankProvider.Parse(...)` in `BankResolver`, wrap in `UnrecognizedLayoutException` so the existing `ExtractionFailureMapper` produces the `UNRECOGNIZED_LAYOUT` error the spec preserves (FR-005, FR-011). Rationale: a parse failure after a positive detection is structurally indistinguishable from "the layout doesn't actually match" from the client's perspective, so reusing the same error code keeps the contract simple. Rejected: introduce a new `BANK_PARSE_FAILED` code — non-additive change to the error taxonomy (violates FR-010).

7. **Registration mechanism.** Choice: per-bank extension method on `IServiceCollection` (e.g. `services.AddBacBank()`). The `BankRegistry` is registered by `AddCardStatementCore()` and resolves an `IEnumerable<IBankProvider>` from DI at construction time, snapshotting an immutable list. Rationale: idiomatic .NET DI; no reflection; banks can declare their own internal services as private without leaking them; adding a bank is literally one line in `Program.cs`. Rejected: reflection-based assembly scanning (magic, breaks AOT, hard to unit-test), a global static registry (mutable singleton, untestable), config-file-driven registration (we don't have hot reload as a goal).

8. **Empty-registry startup failure.** Choice: `BankRegistry`'s constructor throws `EmptyBankRegistryException` when given an empty `IEnumerable<IBankProvider>`. Because DI resolves the registry eagerly when the resolver is constructed (and the resolver is requested by the endpoint on first request), the failure happens loudly at first-request time at the latest; to satisfy SC-007 (the process must not silently come up empty), `Program.cs` calls `app.Services.GetRequiredService<IBankRegistry>()` once at startup right after `app.Build()`, which forces construction and surfaces the exception before `app.Run()` listens on the port. Rationale: satisfies FR-015 / SC-007 without inventing a hosted service. Rejected: `IHostedService` doing the validation (heavier, asynchronous, makes the failure timing fuzzy).

9. **`IStatementParser` keepalive.** Choice: keep the interface as-is, in `Abstractions/`, but stop registering it in DI. Each `IBankProvider` is free to construct its own parser internally (BAC's provider holds an instance of `BacStatementParser` which still implements `IStatementParser`). Rationale: zero churn in places that already test the BAC parser directly (existing parser tests keep working), while the API/endpoint no longer has the wrong-shaped dependency. Rejected: delete `IStatementParser` (large diff for no behavioral gain), make it `IStatementParser<TBank>` generic (purely cosmetic — banks don't share parser implementations anyway).

10. **Where the additive `bank` field goes in the response.** Choice: appended as the **last** field on `ExtractedStatementResponse` (`bank: { id, displayName }`). Rationale: `System.Text.Json` with default settings emits properties in declaration order; appending to the record's parameter list at the end is binary-additive and never re-orders existing JSON keys, satisfying SC-002 (byte-for-byte parity on the existing fields). Rejected: nesting `bank` inside `statement` header (would change a long-standing nested object's shape and risk frontend regressions), top-of-payload (would shift downstream key order on some serializers).

11. **Frontend impact.** Choice: zero edits. The frontend never reads `bank`; it ignores the additive field. Verified by re-running existing frontend tests against the refactored backend. Rationale: explicit spec guarantee (FR-009, "frontend MUST keep working unchanged").

12. **Where the BAC code physically lives.** Choice: delete `src/CardStatement.Core/Parsing/` entirely and move every file into `src/CardStatement.Core/Banks/Bac/`, prefixing each type with `Bac` (e.g. `BacRowClassifier`). Rationale: future banks will need their own row classifier, their own metadata extractor, and their own table locator; the unprefixed names (`RowClassifier`, `StatementMetadataExtractor`) would conflict and would also misleadingly imply they are shared. The git history is preserved via `git mv`. Rejected: leave files in `Parsing/`, just register them under a BAC provider (looks shared, isn't — invites future contributors to "add a case" in `RowClassifier` instead of creating a new bank).

**Output**: `research.md` with all decisions and rejected alternatives recorded.

## Phase 1: Design & Contracts

**Prerequisites**: `research.md` complete ✅

Artifacts produced by this phase (committed to `specs/002-multi-bank-support/`):

1. **`data-model.md`** — concrete shape of `IBankProvider`, `BankInfo`, `BankDetection`, `BankRegistry`, `BankResolver`, `BankResolutionResult`, plus the additive `BankInfoDto` and its slot in `ExtractedStatementResponse`. Field-by-field with semantics, invariants, and which spec FR each piece backs.

2. **`contracts/openapi.yaml`** — copy of `001-pdf-extract-web/contracts/openapi.yaml` with one additive change: `ExtractedStatementResponse` gains a required `bank` field of type `BankInfo { id: string, displayName: string }`. All other schemas (`ExtractionErrorResponse`, `ErrorBody`, `CardholderSection`, `Transaction`, totals) are unchanged. The error-code enum gains zero new values (FR-010).

3. **`quickstart.md`** — how to (a) run the refactored backend against the sample BAC PDF and verify byte-identical output minus the `bank` field, and (b) add a second bank end-to-end in ~15 minutes by following a literal step-by-step recipe (create folder, implement `IBankProvider`, register, write three tests). Doubles as the future bank-author's onboarding doc.

4. **Agent context update** — replace the existing `<!-- SPECKIT START -->` block in `CLAUDE.md` to point at this plan (`specs/002-multi-bank-support/plan.md`) so future Claude sessions in this repo pick up the new structural rules without re-reading the old `001-pdf-extract-web/plan.md`.

### Post-Design Constitution Re-check

Constitution remains unratified ⇒ no gates to re-evaluate. The "one narrow seam per variation point" recommendation surfaced in the pre-check is honored by the design: `IBankProvider` is the single seam, and every bank-varying behavior is reachable only through it.

## Complexity Tracking

No constitution violations to justify. The plan deliberately *avoids* several common over-engineering traps and they are listed here so a reviewer can confirm they were considered and rejected on purpose:

| Avoided complexity | Why rejected for this refactor |
|---|---|
| A new `CardStatement.Banks.Bac` project (separate csproj) | Adds project boundary overhead, slows builds, blocks easy refactoring across the `Banks/` folder. A folder + namespace is enough to enforce the "one folder = one bank" rule. Re-evaluate only when a bank has so many internal dependencies that it warrants packaging separately. |
| A plugin-loader / `Assembly.LoadFrom("banks/*.dll")` | Reflection-based discovery is magic, breaks .NET trimming/AOT, is hard to unit-test, and provides zero value when "add a bank" already means "add code + redeploy". Explicit `services.AddXxxBank()` is one extra line and reads top-to-bottom. |
| Generic `IStatementParser<TBank>` / banking-marker types | Type-system gymnastics with no runtime benefit; banks don't share parser implementations, so the generic parameter never gets reused. |
| A new `BANK_PARSE_FAILED` error code distinct from `UNRECOGNIZED_LAYOUT` | Non-additive change to the error envelope. The client can't act differently between the two — both mean "we can't extract this PDF". |
| Per-bank options classes registered in DI | YAGNI; BAC's `ParsingOptions` is internal to BAC and can stay constructed in BAC's provider until a real configuration need emerges. |
| A "bank versioning" / "layout version" registration concept | Future bank statements changing format is a known problem, but solving it via a registry of `{bank, layoutVersion}` tuples is premature — the bank's own provider can route internally between layouts (and most banks will only ever have one active layout at a time). |
| Per-bank custom error codes | Would force the frontend to learn about banks and would re-open the FR-010 contract. If a bank wants nuanced errors, it logs them; the client sees the same `UNRECOGNIZED_LAYOUT` / `PARSE_FAILED` taxonomy. |
| A hot-reload / runtime registration mechanism | Explicit non-goal (spec "Out of scope"). Adding it would also break the deterministic-startup guarantee in FR-015. |
| Async `IBankProvider.DetectAsync` / `ParseAsync` | All current detectors and parsers are CPU-bound and synchronous (PdfPig itself is sync). Forcing async on the seam would push noise into every implementation and every test. If a future bank needs to call out (e.g. a cross-bank fingerprint service), introduce an async overload then, not now. |
| Cancellation tokens on the seam | Same reasoning as above — single-PDF parses on a developer machine are sub-second; this is not a streaming workload. Re-evaluate if request sizes balloon. |
| A `BankResolver` that returns a discriminated union (matched / unmatched / ambiguous) | Internal `BankResolutionResult` plus thrown `NoBankMatchedException` is plenty: the endpoint cares about success-or-mapped-error, and the resolver already handles ambiguity internally. Surfacing a third state to callers would create dead code. |
