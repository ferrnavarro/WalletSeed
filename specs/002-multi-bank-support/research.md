# Research: Multi-Bank Backend Support

**Phase**: 0 (Outline & Research) | **Feature**: `002-multi-bank-support` | **Date**: 2026-05-29

This document records the design decisions made before any code is written, with the rationale that future contributors (or future-me reading this in six months) need to understand why the refactor looks the way it does. Every decision below is anchored to one or more functional requirements (`FR-NNN`) or success criteria (`SC-NNN`) from [spec.md](./spec.md).

There are **no unresolved `NEEDS CLARIFICATION` items** at the end of this phase.

---

## D1. The seam shape: one interface, not three

**Decision**: Introduce a single interface, `IBankProvider`, that bundles three concerns per bank: **identity** (`BankInfo Info { get; }`), **detection** (`BankDetection Detect(PdfDocumentWords)`), and **parse** (`Statement Parse(PdfDocumentWords)`).

**Rationale**:
- The three concerns are always co-implemented: a bank that can detect but not parse (or vice versa) is a bug, not a use case worth designing for.
- One interface = one registration point per bank. The spec's "add a bank by adding a file" claim (SC-003) is much easier to honor when there is exactly one type to implement and exactly one DI call to make.
- Logging needs to attach the bank's identity to detection outcomes and parse failures alike (FR-017, FR-018). Coupling identity to the same object that carries the behavior keeps the resolver's logging logic trivial.

**Alternatives considered**:
- *Separate `IBankDetector` and `IStatementParser<TBank>` interfaces, registered independently.* Rejected: fragmented (two registrations per bank instead of one), no flexibility actually gained (no bank shares its detector with another bank's parser), more state for the resolver to correlate.
- *A single `IBankProvider.HandleAsync(PdfDocumentWords) -> Result<Statement, NoMatch>` combining detection and parse.* Rejected: it forces every detector to also produce the parsed statement on positive detection, which means a parser exception can no longer be cleanly distinguished from a "did not match" in the resolver — exactly the distinction FR-008 and FR-011 require us to keep.

---

## D2. What detection sees

**Decision**: `IBankProvider.Detect` receives the already-extracted `PdfDocumentWords` (the same `{ PageCount, IReadOnlyList<PdfWord> Words }` record the existing `IPdfExtractor.Extract(path)` produces today). It does **not** receive the raw PDF bytes or the path.

**Rationale**:
- FR-007 forbids re-extracting the PDF per registered bank. Passing the already-extracted words is the only shape that makes "extract once, detect N times" structurally enforced — there is nothing else in scope for a detector to re-extract from.
- Every detector and every parser will end up looking at the same words anyway; sharing them is free.
- Word coordinates are preserved, so a detector can check positional signals (e.g. "is there a `CONCEPTO` header in the central column band on page 1?") without re-running layout analysis. This matters because filename-based or pure-text-based detection is unreliable (filenames don't survive re-uploads; text alone misses banks that share keywords).

**Alternatives considered**:
- *Pass the raw PDF stream / path.* Rejected: would tempt detectors to re-open the PDF (violates FR-007) and introduces I/O into a logically pure function (hurts testability).
- *Filename only.* Rejected: useless when a user renames a download; brittle across browsers.
- *Magic-byte sniff.* Rejected: every PDF starts with `%PDF-`. A few banks watermark their PDFs in metadata, but PdfPig already exposes that as words on page 1 if needed.

---

## D3. The shape of a detection result

**Decision**: `BankDetection { bool Matched, int Confidence, string? Reason }`, with `Confidence` documented to live on a small integer scale (0–100) using three named buckets:
- `BankDetection.NoMatch()` — `Matched = false, Confidence = 0`
- `BankDetection.Match(int confidence, string? reason = null)` — `Matched = true`, `confidence` in `[1, 100]`
- Constants: `BankDetection.HighConfidence = 90` (e.g. exact card-BIN match), `MediumConfidence = 50` (layout match without BIN), `LowConfidence = 10` (single weak signal).

**Rationale**:
- A bare `bool` would force the resolver to break ambiguous claims by registration order, which is fragile: re-ordering `services.AddBacBank()` / `services.AddBankXBank()` in `Program.cs` would silently change behavior on overlapping detection (violates FR-012's deterministic-across-refactors guarantee).
- `Confidence` lets bank authors express "I am almost certainly this PDF" (BIN match) vs "this could be me" (layout-only match) without requiring a global negotiation across banks.
- `Reason` is for logs only (FR-017, FR-018). The resolver writes it into the warning when reporting ambiguity so the operator immediately knows *why* each bank thought it had a match.

**Alternatives considered**:
- *Bare `bool`.* Rejected as above.
- *Floating-point confidence in `[0.0, 1.0]`.* Rejected: equality-comparison surprises (`0.9 + 0.05 != 0.95`); harder to reason about than integer buckets; no real win for a coarse signal.
- *An enum (`NoMatch | Weak | Strong`).* Rejected: too coarse — does not let two strong-matching banks be ordered without falling back to id-lexicographic tie-break anyway, but loses the per-bank ordering info that a number provides.

---

## D4. Ambiguity tie-breaker

**Decision**: When two or more banks return `Matched = true`, pick the one with the highest `Confidence`. If two banks tie on confidence, pick the one with the lexicographically smallest `BankInfo.Id`. The chosen winner is parsed; the ambiguity is logged at warning level with all claimants' `{id, confidence, reason}` triples (FR-006, FR-018).

**Rationale**:
- Highest-confidence-wins matches the spirit of `Confidence`: bank authors use it precisely to declare relative certainty.
- Lexicographic id-order is the only tie-breaker that is genuinely deterministic across **process restarts**, **registration order changes**, and **future refactors** (FR-012). Wall-clock, registration order, GUIDs, or runtime hash codes all violate at least one of these.
- Logging the conflict tells the bank authors exactly what to fix (typically: tighten one bank's detector). Silently picking a winner without logging would mask real bugs.

**Alternatives considered**:
- *Registration order wins.* Rejected: fragile (re-ordering `Program.cs` changes behavior); FR-006 explicitly demands "regardless of registration order changes."
- *Throw on ambiguity.* Rejected: a correct-but-overlapping bank would crash the request — strictly worse than the buggy-detector case FR-008 already covers.
- *Random pick.* Rejected: non-deterministic, kills SC-008.

---

## D5. Detector-throws containment

**Decision**: `BankResolver` wraps each `IBankProvider.Detect(...)` call in a try-catch. Any exception is logged at error level with `{ bank.Info.Id, exception.Message }` and treated as `BankDetection.NoMatch()`. The resolver continues to the next bank in the registry.

**Rationale**:
- FR-008 directly mandates this behavior. The edge case ("a bank's detection logic throws") is also called out in the spec's Edge Cases section.
- Surfacing the error to the bank's author via logs (instead of swallowing silently) means the bug actually gets fixed.

**Alternatives considered**:
- *Rethrow.* Rejected: violates the "one buggy bank can't take down the endpoint" requirement.
- *Mark the bank as dead for the lifetime of the process.* Rejected: stateful; complicates determinism; tempting but unnecessary YAGNI for an iteration where banks are added at code-edit cadence anyway.

---

## D6. Parser-throws containment

**Decision**: `BankResolver` wraps the `IBankProvider.Parse(...)` call (which runs only for the chosen bank) in a try-catch. Any exception is logged at warning level (with the bank id) and rethrown as `UnrecognizedLayoutException`, which the existing `ExtractionFailureMapper` already converts to `UNRECOGNIZED_LAYOUT` (HTTP 422).

**Rationale**:
- FR-011 mandates that bank-parse failures map to the existing error envelope, never leak internal exception types, and not introduce new error codes (FR-010).
- A parse failure after positive detection is, from the client's perspective, indistinguishable from "the layout didn't actually match" — same remediation (try a different PDF), same UX, so reusing the same error code is correct.

**Alternatives considered**:
- *Introduce `PARSE_FAILED_AFTER_DETECT` or `BANK_PARSE_FAILED`.* Rejected: non-additive change to the public error taxonomy; violates FR-010 and the spec's "no new error codes" Out-of-Scope clause.
- *Let the exception propagate to the global `app.UseExceptionHandler`.* Rejected: that handler returns generic `PARSE_FAILED` (500), which is technically allowed but worse: 422 with a recognizable code is more useful to the client than 500 with the catch-all code.

---

## D7. Registration mechanism

**Decision**: Each bank ships an `IServiceCollection` extension method named `AddXxxBank()` (e.g. `AddBacBank()`). It registers the `IBankProvider` and any internal services the bank needs. The core ships a separate `AddCardStatementCore()` extension method that registers `IPdfExtractor`, `IReconciler`, `BankRegistry`, and `BankResolver` — but no banks. `Program.cs` calls both:

```csharp
builder.Services.AddCardStatementCore();
builder.Services.AddBacBank();
// builder.Services.AddBankXBank(); // future
```

`BankRegistry` takes `IEnumerable<IBankProvider>` in its constructor, snapshots it into an `ImmutableArray<IBankProvider>` once, and throws `EmptyBankRegistryException` if the collection is empty.

**Rationale**:
- Idiomatic .NET DI; no reflection; works under trimming/AOT.
- One line per bank in `Program.cs` — the literal "add a bank" gesture.
- Each bank can register its own internal services (e.g. `BacDetector`, `BacStatementParser`) without exposing them on the shared `Abstractions/` surface.
- `BankRegistry` is the single place where the non-empty invariant is enforced; tests can target this directly (SC-007).
- Splitting `AddCardStatementCore()` from `AddXxxBank()` lets test harnesses opt into a partial setup (e.g. core only, plus a stub bank).

**Alternatives considered**:
- *Reflection-based discovery (`Assembly.GetTypes().Where(t => typeof(IBankProvider).IsAssignableFrom(t))`).* Rejected: magical, breaks AOT/trimming, untraceable in tooling, makes "which banks are active?" a runtime mystery.
- *Global static registry (`BankRegistry.Register(new BacBankProvider())` at startup).* Rejected: mutable singleton; survives across test instances; un-mockable.
- *Config file (`banks.yml` listing class names).* Rejected: invents a new config surface for zero benefit; runtime errors instead of compile-time; not a hot-reload feature anyway (explicitly out of scope).

---

## D8. Empty-registry startup failure

**Decision**: `BankRegistry`'s constructor throws `EmptyBankRegistryException` on empty input. `Program.cs` calls `app.Services.GetRequiredService<IBankRegistry>()` exactly once after `app.Build()` and before `app.Run()` — this eagerly constructs the registry, surfacing any `EmptyBankRegistryException` before the process starts listening. The eager resolve also serves as the natural place to emit the "registered banks at startup" log line FR-016 requires.

**Rationale**:
- Satisfies FR-015 / SC-007 ("starting with zero registered banks MUST fail loudly at startup").
- No new abstractions invented (no `IHostedService`, no `IStartupValidator`).
- The eager resolve is also the natural log point for "Registered banks: [bac, ...]" so FR-016 happens for free in the same place.

**Alternatives considered**:
- *Lazy validation on first request.* Rejected: process appears "healthy" until the first PDF arrives; defeats SC-007's "the process does not enter a state where it accepts HTTP requests."
- *`IHostedService.StartAsync` validation.* Rejected: heavier, asynchronous, complicates failure attribution, and `IHostedService` failures are not always fatal depending on host configuration.

---

## D9. Keep `IStatementParser` as an interface, stop registering it in DI

**Decision**: Leave `IStatementParser` in `Abstractions/` exactly as it is. Each `IBankProvider` constructs its own parser internally (BAC's provider holds an instance of `BacStatementParser : IStatementParser`). DI no longer has an `IStatementParser` binding.

**Rationale**:
- Zero churn in the existing parser unit tests (they still test the concrete class directly).
- Removes the wrong-shaped dependency from the API layer (it shouldn't depend on a single parser interface in a multi-bank world).
- Keeps the interface as a useful type for testing and for future bank implementations to opt into.

**Alternatives considered**:
- *Delete `IStatementParser`.* Rejected: needless churn; the type is still meaningful as a per-bank pattern.
- *Make it `IStatementParser<TBank>` with a marker type.* Rejected: type-system gymnastics with no runtime benefit; banks don't share parser implementations.

---

## D10. The additive `bank` field on the response

**Decision**: Append `BankInfoDto Bank` as the **last** parameter of `ExtractedStatementResponse`'s positional record. `BankInfoDto` is `{ string Id, string DisplayName }`. JSON output therefore gains exactly one trailing key, `"bank": { "id": "bac", "displayName": "BAC Credomatic (El Salvador)" }`, after the existing keys, in the existing order.

**Rationale**:
- `System.Text.Json` emits record properties in declaration order; appending preserves the byte sequence of every existing key. SC-002 ("byte-for-byte on every field that existed before this spec") is satisfied by construction.
- The frontend never reads `bank` and will ignore extra fields by default (FR-009).
- A future bank UI can opt into displaying "extracted from <displayName>" without backend changes.

**Alternatives considered**:
- *Nest `bank` inside the statement header.* Rejected: changes the shape of a long-standing nested object — riskier diff, harder to roll back, and would re-order header keys.
- *Place `bank` at the top of the payload.* Rejected: shifts the JSON key order; some serializers / pretty-printers / snapshot tests would notice.
- *Make `bank` optional / nullable.* Rejected: we always know the bank when the response is built (resolution is what produced the statement), so optional adds a nullable for no caller benefit. SC-002 talks about *fields that existed before* — the new field's required-ness is not a regression.

---

## D11. Frontend impact

**Decision**: Zero edits to `frontend/`. The frontend continues to send a multipart upload to `/api/statements/extract` and to consume the existing fields. Extra fields are silently ignored.

**Rationale**:
- FR-009 explicit guarantee ("the frontend MUST keep working unchanged").
- Verified by SC-001 (existing tests pass) and SC-002 (byte-for-byte on existing fields).

**Alternatives considered**:
- *Update the frontend to show the bank name.* Out of scope; explicitly deferred in the spec.

---

## D12. Physical home of the BAC code

**Decision**: Delete `src/CardStatement.Core/Parsing/` entirely. Move every file into `src/CardStatement.Core/Banks/Bac/`, prefixing each type with `Bac` (e.g. `RowClassifier` → `BacRowClassifier`, `StatementMetadataExtractor` → `BacMetadataExtractor`, `TransactionTableLocator` → `BacTransactionTableLocator`). Also move the BAC-specific helpers currently sitting in `Pdf/` (`RowBuilder.cs`, `TransactionTableLocator.cs`, `ParsingOptions.cs`) into `Banks/Bac/`. `PdfPigExtractor.cs` and `TableLayout.cs` stay in `Pdf/` because they are bank-agnostic. Use `git mv` so blame/history follow.

**Rationale**:
- Future banks will need their own row classifier, their own metadata extractor, and their own table locator. The unprefixed names would either collide (forcing renames later) or, worse, tempt a future contributor to *add a `case "BAC X"`* inside `RowClassifier.cs` instead of creating a new bank. The Bac-prefixed names make the seam visually obvious at every call site.
- "One folder = one bank" is the cheapest mental model we can give a future contributor.
- `Pdf/PdfPigExtractor.cs` and `Pdf/TableLayout.cs` are reused across banks; they stay in `Pdf/` exactly because they are not BAC-specific. (A future bank's table locator can return its own `TableLayout` instance.)

**Alternatives considered**:
- *Keep BAC files in `Parsing/`, just register them under a BAC provider.* Rejected: looks shared, isn't. Encourages additions-in-place instead of new-bank-folders, defeating the entire refactor.
- *Move BAC into its own csproj (`src/CardStatement.Bac.Core/`).* Rejected: project boundary overhead with no compensating benefit at this scale. Re-evaluate if BAC develops large internal subsystems.

---

## D13. Test organization

**Decision**:
- Per-bank tests live under `tests/CardStatement.Tests/Banks/<BankName>/`. The existing BAC parser tests (currently under `tests/CardStatement.Tests/`) move into `tests/CardStatement.Tests/Banks/Bac/` and their `using` directives are updated for the new namespace.
- Bank-agnostic infrastructure tests (`BankRegistryTests`, `BankResolverTests`) live at the top level of `tests/CardStatement.Tests/`.
- Endpoint-level integration tests (`ExtractEndpointTests`, `MultiBankRoutingTests`, `EmptyRegistryStartupTests`) live in `tests/CardStatement.Api.Tests/`.
- `StubBankProvider` lives in `tests/CardStatement.Api.Tests/Fixtures/` and is used by `MultiBankRoutingTests` (and is also a worked example for future contributors of what an `IBankProvider` looks like in isolation).

**Rationale**:
- Mirrors the production layout (`Banks/<BankName>/`), so adding a bank means adding both a code folder and a test folder, side by side.
- Bank-agnostic tests next to bank tests would obscure the seam — the registry/resolver tests don't care about BAC.
- The stub bank is a test fixture, not production code; keeping it under `Fixtures/` makes its role obvious.

**Alternatives considered**:
- *Co-locate tests with code (`src/CardStatement.Core/Banks/Bac/Tests/...`).* Rejected: not idiomatic for .NET projects; complicates csproj include patterns.

---

## D14. Reconciler stays bank-agnostic (re-affirmation)

**Decision**: `Reconciler` keeps its current shape: it consumes a `Statement` (already populated with computed-from-rows totals and printed-from-PDF totals) and returns a `Statement` with `ReconciliationStatus` filled in per section and at the root. It is registered by `AddCardStatementCore()` and runs *after* the bank-specific parser, on the `Statement` the bank produced.

**Rationale**:
- Explicit assumption in the spec ("the existing reconciliation engine is bank-agnostic"). Future banks that need bank-specific reconciliation tolerances are a separate spec.
- Keeping reconciliation outside `IBankProvider` means each bank author writes less code; they only need to populate `PrintedSubtotalCharges`/`PrintedSubtotalCredits` correctly.

**Alternatives considered**:
- *Move reconciliation inside the bank provider (`IBankProvider.Parse` returns a fully-reconciled `Statement`).* Rejected: invites per-bank reconciliation logic to drift; harder to enforce the spec's "computed = sum of rows" invariant uniformly.

---

## Summary: all spec FRs / SCs are traceable to a decision

| Spec item | Anchor decision(s) |
|---|---|
| FR-001 (bank as registered unit with id / name / detect / parse) | D1, D3, D7 |
| FR-002 (add a bank without modifying existing code) | D1, D7, D12 |
| FR-003 (BAC shipped, behavior identical) | D9, D12, D14 |
| FR-004 (per-upload single bank selected via content) | D2, D4 |
| FR-005 (no match → UNRECOGNIZED_LAYOUT) | D6 |
| FR-006 (deterministic tie-breaker + logged ambiguity) | D4 |
| FR-007 (no per-bank re-extraction) | D2 |
| FR-008 (detector-throws contained) | D5 |
| FR-009 (endpoint contract preserved, bank field additive) | D10, D11 |
| FR-010 (no new error codes) | D6 |
| FR-011 (parser-throws → existing error envelope) | D6 |
| FR-012 (deterministic across restarts and registration order) | D4, D7 |
| FR-013 (concurrency safe) | D7 (immutable registry snapshot) |
| FR-014 (banks can't mutate shared state) | D1, D7 |
| FR-015 (empty registry fails loudly) | D7, D8 |
| FR-016 (startup log of registered banks) | D8 |
| FR-017 (per-request bank log without PII) | D2 (no raw bytes in detection), D8 |
| FR-018 (logs distinguish single / ambiguous / none) | D4, D5 |
| SC-001 (existing tests pass) | D9, D10, D11, D12 |
| SC-002 (byte-for-byte on existing fields) | D10 |
| SC-003 (add a bank = only new files + one registration edit) | D1, D7, D12 |
| SC-004 (three-way routing test passes) | D1, D4, D5, D6 |
| SC-005 (≤ +10% latency for second bank) | D2 (extract-once) |
| SC-006 (broken bank doesn't kill BAC) | D5 |
| SC-007 (empty registry fails at startup) | D8 |
| SC-008 (same PDF → same bank, repeatedly) | D2, D4 |
