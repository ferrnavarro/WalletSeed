# Tasks: Multi-Bank Backend Support

**Input**: Design documents from `/specs/002-multi-bank-support/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/openapi.yaml ✅, quickstart.md ✅

**Tests**: Test tasks **are** included. Spec success criteria (SC-001, SC-004, SC-006, SC-007, SC-008) are explicitly framed as automated tests, so each one gets a corresponding task. Existing `001-pdf-extract-web` test suites are treated as a regression baseline and must keep passing throughout.

**Organization**: Tasks are grouped by user story so each story can be implemented, tested, and demoed independently. The seam infrastructure built in **Phase 2 (Foundational)** is the only blocker shared across all three stories — once it compiles, US1, US2, and US3 can proceed in parallel (though most teams will do US1 first since it doubles as the regression gate that protects everything else).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: `[US1]` / `[US2]` / `[US3]` — maps task to the user story it serves. Setup, Foundational, and Polish tasks have no story label.
- All file paths are repository-relative.

## Path Conventions

- Backend code: `src/CardStatement.Core/`, `src/CardStatement.Api/`, `src/CardStatement.App/`
- Backend tests: `tests/CardStatement.Tests/`, `tests/CardStatement.Api.Tests/`
- Frontend: **NOT TOUCHED** in this spec (FR-009). Any task that edits anything under `frontend/` is a bug in this plan.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Capture a pre-refactor baseline so SC-002 (byte-for-byte parity on existing fields) can actually be verified after the work lands.

- [x] T001 Verify the current `main` branch builds and all existing tests pass before any refactor work begins, by running `dotnet build CreditStatementParser.slnx && dotnet test CreditStatementParser.slnx` from the repo root. Record the test count and pass/fail summary in this task's commit message — this is the baseline SC-001 must match after the refactor.
- [x] T002 Capture the pre-refactor JSON baseline for the sample BAC PDF by running the existing API against `samples/final5140_45178439_316493_0.pdf` and saving the response to `specs/002-multi-bank-support/baselines/extract-001-baseline.json` (create the `baselines/` folder). This file is the ground-truth for SC-002 and MUST be checked in. Do NOT regenerate it after starting the refactor.

**Checkpoint**: Baseline captured. Refactor can begin without losing the ability to verify regression.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build the entire bank-agnostic seam (interfaces, registry, resolver, exceptions, the additive DTO slot, the DI extension) **without registering any bank yet**. At the end of this phase the project does not run — the API has no bank registered — but every shared type that all three user stories depend on exists and is unit-tested in isolation.

**⚠️ CRITICAL**: No user story (US1/US2/US3) work can begin until this phase is complete. Concretely: BAC cannot be moved into the new `Banks/Bac/` folder until `IBankProvider` exists.

### Core types

- [x] T003 [P] Create `src/CardStatement.Core/Banks/BankInfo.cs` defining `public sealed record BankInfo(string Id, string DisplayName)` with constructor guards (non-null/whitespace; `Id` matches `^[a-z0-9][a-z0-9-]{0,31}$`) per data-model.md §1.
- [x] T004 [P] Create `src/CardStatement.Core/Banks/BankDetection.cs` defining `public sealed record BankDetection(bool Matched, int Confidence, string? Reason)` with `HighConfidence = 90`, `MediumConfidence = 50`, `LowConfidence = 10` constants and the `NoMatch()` / `Match(int, string?)` factory methods per data-model.md §2. Enforce: `Matched=true ⇒ Confidence ∈ [1,100]`; `Matched=false ⇒ Confidence == 0`.
- [x] T005 [P] Create `src/CardStatement.Core/Abstractions/IBankProvider.cs` defining `BankInfo Info { get; }`, `BankDetection Detect(PdfDocumentWords words)`, `Statement Parse(PdfDocumentWords words)` per data-model.md §3.
- [x] T006 [P] Create `src/CardStatement.Core/Abstractions/IBankRegistry.cs` defining `IReadOnlyList<IBankProvider> Providers { get; }` per data-model.md §4.
- [x] T007 [P] Create `src/CardStatement.Core/Abstractions/IBankResolver.cs` defining `(BankInfo Bank, Statement Statement) Resolve(PdfDocumentWords words)` per data-model.md §6.
- [x] T008 [P] Create `src/CardStatement.Core/Banks/BankResolutionResult.cs` defining `internal sealed record BankResolutionResult(IBankProvider Provider, Statement Statement)` per data-model.md §5.

### Exceptions

- [x] T009 [P] Create `src/CardStatement.Core/Banks/Exceptions/NoBankMatchedException.cs` per data-model.md §7. Single parameterless ctor with a fixed message.
- [x] T010 [P] Create `src/CardStatement.Core/Banks/Exceptions/EmptyBankRegistryException.cs` per data-model.md §7. Message explicitly names `services.AddBacBank()` so the failure is self-documenting.
- [x] T011 [P] Create `src/CardStatement.Core/Banks/Exceptions/DuplicateBankIdException.cs` per data-model.md §7. Carries `IReadOnlyList<string> DuplicateIds`.

### Registry and resolver

- [x] T012 Create `src/CardStatement.Core/Banks/BankRegistry.cs` implementing `IBankRegistry` per data-model.md §4. Constructor takes `IEnumerable<IBankProvider>`, snapshots to `ImmutableArray`, throws `EmptyBankRegistryException` on empty input and `DuplicateBankIdException` on duplicate ids (group by `Info.Id` with `StringComparer.Ordinal`). (Depends on T005, T006, T010, T011.)
- [x] T013 Create `src/CardStatement.Core/Banks/BankResolver.cs` implementing `IBankResolver` per data-model.md §6 and research.md D4–D6. Algorithm: iterate `registry.Providers`, wrap each `Detect` in try/catch (log error + treat as `NoMatch` per FR-008), collect candidates; if zero throw `NoBankMatchedException`; if more than one log a warning listing all `{id, confidence, reason}` triples and tie-break by `(-Confidence, Provider.Info.Id ordinal)`; wrap winner's `Parse` in try/catch and rethrow as `UnrecognizedLayoutException` on failure (FR-011). Take `ILogger<BankResolver>` and `IBankRegistry` via ctor. (Depends on T003, T005, T006, T007, T009.)

### DI composition

- [x] T014 Create `src/CardStatement.Core/Registration/CoreServiceCollectionExtensions.cs` with `public static IServiceCollection AddCardStatementCore(this IServiceCollection services)` that registers `IPdfExtractor → PdfPigExtractor`, `IReconciler → Reconciler`, `IBankRegistry → BankRegistry`, `IBankResolver → BankResolver` all as `TryAddSingleton` per data-model.md §9. **Does not register any `IBankProvider`** — that is the bank's own responsibility. (Depends on T012, T013.)

### Additive response field plumbing

- [x] T015 [P] Create `src/CardStatement.Api/Contracts/BankInfoDto.cs` defining `public sealed record BankInfoDto(string Id, string DisplayName)` per data-model.md §8.
- [x] T016 Update `src/CardStatement.Api/Contracts/ExtractedStatementResponse.cs` to append `BankInfoDto Bank` as the **LAST** record parameter (after `UnmappedCards`). The order is load-bearing — see research.md D10 / SC-002. (Depends on T015.)
- [x] T017 Update `src/CardStatement.Api/Mapping/StatementMapper.cs` so `ToResponse` takes `(Statement statement, BankInfo bank)` and emits the new `Bank` property as `new BankInfoDto(bank.Id, bank.DisplayName)`. All other mapping logic is unchanged. (Depends on T015, T016.)
- [x] T018 Update `src/CardStatement.Api/ErrorHandling/ExtractionFailureMapper.cs` to map `NoBankMatchedException` to 422 `UNRECOGNIZED_LAYOUT` per data-model.md §7. Generalize the `UnrecognizedLayoutException` user message to remove the BAC-specific wording (drop "as a BAC Credomatic statement"). All other mappings unchanged (FR-010). (Depends on T009.)

### Foundational tests (run with `dotnet test`; verify they fail before implementing the things under test where applicable)

- [x] T019 [P] Create `tests/CardStatement.Tests/Banks/BankInfoTests.cs` covering: rejects null/whitespace `Id` or `DisplayName`; rejects `Id` not matching `^[a-z0-9][a-z0-9-]{0,31}$` (uppercase, underscores, leading hyphen, too long); accepts valid ids. (Depends on T003.)
- [x] T020 [P] Create `tests/CardStatement.Tests/Banks/BankDetectionTests.cs` covering: `NoMatch()` has `Matched=false, Confidence=0`; `Match(N, reason)` has `Matched=true, Confidence=N`; constructor rejects `Matched=true, Confidence=0` and `Matched=false, Confidence>0` and out-of-range confidences. (Depends on T004.)
- [x] T021 [P] Create `tests/CardStatement.Tests/Banks/BankRegistryTests.cs` covering: empty `IEnumerable` throws `EmptyBankRegistryException` (SC-007); duplicate `Info.Id` (case-sensitive ordinal) throws `DuplicateBankIdException` listing the duplicates; normal input snapshots to immutable list in registration order; mutating the source list after construction has no effect. Uses small in-test fake `IBankProvider` records. (Depends on T012.)
- [x] T022 [P] Create `tests/CardStatement.Tests/Banks/BankResolverTests.cs` covering the five canonical resolver scenarios with in-test fake `IBankProvider` implementations and a captured `ILogger<BankResolver>` (`xunit.LoggerFactory` or `Microsoft.Extensions.Logging.Testing`):
  - *Single match* → returns `(bank, statement)`.
  - *No match* → throws `NoBankMatchedException`.
  - *Ambiguous match* → returns the highest-confidence bank; on equal confidence returns the lexicographically-smallest `Id`; logs a warning containing all `{id, confidence, reason}` claimants.
  - *Detector throws* → logged at error level with the bank id, treated as `NoMatch`; other banks still evaluated (SC-006).
  - *Parser throws* → logged at warning level with the bank id, wrapped as `UnrecognizedLayoutException` with the original exception as `InnerException` (FR-011).
  (Depends on T013.)

**Checkpoint**: The seam compiles. All foundational tests pass. `BankRegistry` cannot be constructed without at least one bank (empty registry test green). No bank is registered yet — the API project will fail to start because of this; that is correct and expected at this checkpoint.

---

## Phase 3: User Story 1 — Existing BAC extraction continues to work unchanged (Priority: P1) 🎯 MVP

**Goal**: Move every BAC-specific file into the new `Banks/Bac/` folder, expose BAC through the `IBankProvider` seam, wire DI to use the resolver, and re-prove byte-for-byte regression against the baseline captured in T002. After this phase the application is fully functional again, with exactly one registered bank, producing the same output as before this work plus an additive `bank` field.

**Independent Test**: `dotnet test CreditStatementParser.slnx` shows the same set of pre-existing tests passing. `curl … | jq 'del(.bank)' | diff baselines/extract-001-baseline.json -` shows an empty diff (SC-002). Response includes `bank: { id: "bac", displayName: "BAC Credomatic (El Salvador)" }`.

### Move BAC code into `Banks/Bac/` (preserve git history with `git mv`)

- [x] T023 [US1] Create the new folder `src/CardStatement.Core/Banks/Bac/`. Move and rename each BAC-specific file via `git mv`, renaming the type inside each file to match (the per-file changes are *just* the rename + the namespace; no logic changes). Files to move:
  - `src/CardStatement.Core/Parsing/StatementParser.cs` → `src/CardStatement.Core/Banks/Bac/BacStatementParser.cs` (type renamed `StatementParser` → `BacStatementParser`)
  - `src/CardStatement.Core/Parsing/RowClassifier.cs` → `src/CardStatement.Core/Banks/Bac/BacRowClassifier.cs` (type renamed; `ClassifiedRow` and `ClassifiedRowKind` move too — they are BAC-specific row taxonomies)
  - `src/CardStatement.Core/Parsing/StatementMetadataExtractor.cs` → `src/CardStatement.Core/Banks/Bac/BacMetadataExtractor.cs` (type renamed; the inner `StatementMetadata` record moves too)
  - `src/CardStatement.Core/Parsing/TransactionRowParser.cs` → `src/CardStatement.Core/Banks/Bac/BacTransactionRowParser.cs`
  - `src/CardStatement.Core/Parsing/TransactionDateResolver.cs` → `src/CardStatement.Core/Banks/Bac/BacTransactionDateResolver.cs`
  - `src/CardStatement.Core/Parsing/AmountParser.cs` → `src/CardStatement.Core/Banks/Bac/BacAmountParser.cs`
  - `src/CardStatement.Core/Parsing/SpanishMonths.cs` → `src/CardStatement.Core/Banks/Bac/BacSpanishMonths.cs`
  - `src/CardStatement.Core/Pdf/TransactionTableLocator.cs` → `src/CardStatement.Core/Banks/Bac/BacTransactionTableLocator.cs`
  - `src/CardStatement.Core/Pdf/RowBuilder.cs` → `src/CardStatement.Core/Banks/Bac/BacRowBuilder.cs`
  - `src/CardStatement.Core/Pdf/ParsingOptions.cs` → `src/CardStatement.Core/Banks/Bac/BacParsingOptions.cs`
  All moved types adopt the namespace `CardStatement.Core.Banks.Bac`. `src/CardStatement.Core/Pdf/PdfPigExtractor.cs` and `src/CardStatement.Core/Pdf/TableLayout.cs` stay in `Pdf/` (bank-agnostic). Delete the now-empty `src/CardStatement.Core/Parsing/` folder.
- [x] T024 [US1] Update every `using` directive and internal type reference inside the moved files (and inside any tests under `tests/CardStatement.Tests/` that referenced the old `CardStatement.Core.Parsing` / `CardStatement.Core.Pdf` types being moved) to the new namespaces and renamed types. The behavior inside each file is otherwise unchanged. (Depends on T023.)

### Create the BAC provider, detector, and DI extension

- [x] T025 [US1] Create `src/CardStatement.Core/Banks/Bac/BacDetector.cs` containing the logic that decides "is this PDF a BAC Credomatic statement?". Encapsulate the two signals the existing parser implicitly relies on:
  - A word on page 1 matching the BAC BIN pattern (regex `^459378XXXXXX\d{4}$`, currently in `BacRowClassifier` and `BacStatementParser`).
  - The column-header trio `CONCEPTO` + `CARGOS` + `ABONOS` on the same row of any page (already required by `BacTransactionTableLocator.TryLocate`).
  Return `BankDetection.Match(HighConfidence, "BIN 459378 + CONCEPTO/CARGOS/ABONOS table header found")` if both signals present; `BankDetection.Match(MediumConfidence, "CONCEPTO/CARGOS/ABONOS table header found without BIN")` if only the column header trio; otherwise `BankDetection.NoMatch()`. Pure function, takes `PdfDocumentWords`. (Depends on T004, T023.)
- [x] T026 [US1] Create `src/CardStatement.Core/Banks/Bac/BacBankProvider.cs` implementing `IBankProvider`. Holds `private static readonly BankInfo TheBank = new("bac", "BAC Credomatic (El Salvador)")`. Constructs and owns `BacDetector` and `BacStatementParser` instances (singletons for the lifetime of the provider — they are themselves stateless). `Detect` delegates to `BacDetector`; `Parse` delegates to `BacStatementParser`. (Depends on T005, T025.)
- [x] T027 [US1] Create `src/CardStatement.Core/Banks/Bac/BacServiceCollectionExtensions.cs` with `public static IServiceCollection AddBacBank(this IServiceCollection services)` that calls `services.AddSingleton<IBankProvider, BacBankProvider>()` per data-model.md §9. Uses `AddSingleton` (not `TryAddSingleton`) so double-registration surfaces as `DuplicateBankIdException` at startup, which is the friendlier failure for a copy-paste mistake. (Depends on T026.)

### Wire the API endpoint to use the resolver

- [x] T028 [US1] Update `src/CardStatement.Api/Endpoints/ExtractEndpoint.cs` to depend on `IBankResolver` instead of `IStatementParser`. Replace the existing parse path:
  - Old: `var statement = parser.Parse(words);` + the wrap-in-`UnrecognizedLayoutException` block.
  - New: `var (bank, statement) = resolver.Resolve(words);` — `NoBankMatchedException` and `UnrecognizedLayoutException` already flow into `ExtractionFailureMapper` (FR-005, FR-011).
  After reconciliation, call `StatementMapper.ToResponse(reconciled, bank)`. Log the selected bank id alongside existing per-request log fields (FR-017) — e.g. `log.LogInformation("Bank selected: {BankId}", bank.Id)`. (Depends on T013, T017, T018.)
- [x] T029 [US1] Update `src/CardStatement.Api/Program.cs` to:
  - Replace the three `AddSingleton<IPdfExtractor, …>()` / `AddSingleton<IStatementParser, …>()` / `AddSingleton<IReconciler, …>()` lines with `builder.Services.AddCardStatementCore();`
  - Add `builder.Services.AddBacBank();` immediately after.
  - After `var app = builder.Build();` and before `app.Run();`, eagerly resolve the registry to surface startup failures and emit the FR-016 log line: `var registry = app.Services.GetRequiredService<IBankRegistry>(); app.Logger.LogInformation("Registered banks: {Banks}", string.Join(", ", registry.Providers.Select(p => $"{p.Info.Id} ({p.Info.DisplayName})")));`
  (Depends on T014, T027, T028.)
- [x] T030 [US1] Update `src/CardStatement.App/Program.cs` DI bootstrap to use `AddCardStatementCore()` + `AddBacBank()` in place of the existing `IStatementParser` / `IPdfExtractor` / `IReconciler` registrations. The CLI app's behavior is otherwise unchanged. (Depends on T014, T027.)

### Move and reconcile existing BAC tests

- [x] T031 [US1] Move the existing BAC parser / classifier / metadata-extractor / table-locator unit tests from their current locations under `tests/CardStatement.Tests/` into `tests/CardStatement.Tests/Banks/Bac/`, mirroring the production layout per plan.md §Project Structure. Use `git mv` and update each file's namespace + `using` directives for the renamed types from T023/T024. The test logic is unchanged. (Depends on T024.)
- [x] T032 [US1] Create `tests/CardStatement.Tests/Banks/Bac/BacDetectorTests.cs` with at least these cases: (a) the sample PDF's words (via the existing `IPdfExtractor` and `samples/final5140_45178439_316493_0.pdf`) → `Match(HighConfidence)`; (b) synthetic empty `PdfDocumentWords` → `NoMatch`; (c) synthetic words containing `CONCEPTO/CARGOS/ABONOS` row but no BIN → `Match(MediumConfidence)`. (Depends on T025.)
- [x] T033 [US1] Create `tests/CardStatement.Tests/Banks/Bac/BacBankProviderTests.cs` covering: `Info.Id == "bac"`; `Info.DisplayName == "BAC Credomatic (El Salvador)"`; `Detect` against the sample PDF returns `Matched=true`; `Parse` against the sample PDF returns a `Statement` whose section count, transaction count per section, and per-row values match a small set of representative assertions (use the same fixtures the existing parser tests use). (Depends on T026, T031.)

### Endpoint regression + additive `bank` field

- [x] T034 [US1] Update `tests/CardStatement.Api.Tests/ExtractEndpointTests.cs`: existing assertions on the success response stay unchanged (this is the regression gate for SC-001); add one new assertion that the response JSON includes `"bank": { "id": "bac", "displayName": "BAC Credomatic (El Salvador)" }`. The existing error-path tests (invalid file type, password-protected, no text extractable, unrecognized layout, file too large) stay unchanged and MUST keep passing — this is FR-010 in action. (Depends on T028, T029.)
- [x] T035 [US1] Create `tests/CardStatement.Api.Tests/BacByteParityTests.cs` with a single test that: (a) POSTs `samples/final5140_45178439_316493_0.pdf` to the in-process API via `WebApplicationFactory`; (b) deserializes the response to `JsonNode`, removes the `bank` property; (c) loads `specs/002-multi-bank-support/baselines/extract-001-baseline.json`; (d) asserts the two normalized JSON strings (serialized with the same `JsonSerializerOptions` the API uses) are equal — this is SC-002's automated form. (Depends on T002, T028, T029.)

**Checkpoint**: `dotnet build && dotnet test` passes for the full solution. `dotnet run --project src/CardStatement.Api` starts up with the log line `Registered banks: bac (BAC Credomatic (El Salvador))`. Posting the sample PDF returns byte-identical existing fields plus the new `bank` field. **Phase 3 is the project's MVP** — if you stop here, you have a functioning multi-bank-capable backend that ships exactly one bank (BAC) and proves the refactor preserved behavior.

---

## Phase 4: User Story 2 — A new bank can be added without modifying existing bank code (Priority: P2)

**Goal**: Prove via a test fixture (a `StubBankProvider` that lives only in the test project) that adding a second bank to the registry requires **zero edits** to anything BAC-related, the shared statement model, the endpoint, the reconciler, the response DTOs, or the error mapping. The proof is automated (a passing test) and structurally enforced (the test only references types from `Abstractions/` and `Models/` — never `Banks/Bac/`).

**Independent Test**: `dotnet test --filter FullyQualifiedName~MultiBankRoutingTests` passes. Inspecting the staged diff shows the only files touched in this phase are under `tests/CardStatement.Api.Tests/`. SC-003 is the manual companion verification — running `git diff --stat <merge-base> -- 'src/CardStatement.Core/Banks/Bac/*' 'src/CardStatement.Core/Models/*' 'src/CardStatement.Api/Endpoints/*' 'src/CardStatement.Api/Mapping/*' 'src/CardStatement.Api/Contracts/*' 'src/CardStatement.Api/ErrorHandling/*' 'src/CardStatement.Core/Reconciliation/*'` after this phase shows zero lines added/removed.

### Test fixture (the stub bank)

- [x] T036 [P] [US2] Create `tests/CardStatement.Api.Tests/Fixtures/StubBankProvider.cs` implementing `IBankProvider` with `Info = new("stub", "Stub Test Bank")`. `Detect` returns `Match(HighConfidence)` iff any word in the input equals `__STUB_BANK__` (use `StringComparer.Ordinal`); otherwise `NoMatch`. `Parse` returns a minimal valid `Statement` (one fake `CardholderSection` containing one fake `Transaction`) — enough to exercise the response shape. Co-locate `StubBankServiceCollectionExtensions.AddStubBank(this IServiceCollection)` in the same file. This is the worked-example "second bank" that every US2/US3 test will share.
- [x] T037 [P] [US2] Add a tiny `Fixtures/Pdfs/stub-marker.pdf` to `tests/CardStatement.Api.Tests/` — a one-page text-based PDF whose extractable words include `__STUB_BANK__`. Generate it once via any PDF tool (e.g. `pandoc -t pdf` or a manual export) and commit it as a binary fixture. Document the generation method in a sibling `README.md` so it can be regenerated if ever needed.

### Routing test that proves additive-only

- [x] T038 [US2] Create `tests/CardStatement.Api.Tests/MultiBankRoutingTests.cs` using a custom `WebApplicationFactory<Program>` subclass that calls `services.AddStubBank()` *in addition to* the default `AddBacBank()`. Tests:
  - Posting the BAC sample PDF returns 200 with `bank.id == "bac"` (regression: US1 still holds with a second bank registered — SC-006).
  - Posting `Fixtures/Pdfs/stub-marker.pdf` returns 200 with `bank.id == "stub"`.
  - Posting an empty/blank PDF (or a tiny PDF with neither BAC nor stub markers — create as `Fixtures/Pdfs/neither.pdf` if needed) returns 422 with `error.code == "UNRECOGNIZED_LAYOUT"`.
  (Depends on T029, T036, T037.)

### Structural proof (no edits to existing files)

- [x] T039 [US2] Add a CI-friendly assertion script at `specs/002-multi-bank-support/scripts/verify-additive-only.sh` (and make it executable) that runs `git diff --name-only origin/main -- 'src/CardStatement.Core/Banks/Bac/' 'src/CardStatement.Core/Models/' 'src/CardStatement.Core/Reconciliation/' 'src/CardStatement.Api/Endpoints/' 'src/CardStatement.Api/Mapping/' 'src/CardStatement.Api/Contracts/ExtractedStatementResponse.cs' 'src/CardStatement.Api/Contracts/ErrorCodes.cs' 'src/CardStatement.Api/ErrorHandling/'` and exits non-zero if any file is listed *for changes made after Phase 3 lands* (so this script runs against a baseline that already includes the Phase 3 edits to those files). Document its use in the script header. This is the executable form of SC-003.

**Checkpoint**: A second bank is verifiably addable without touching BAC, the model, the endpoint, the reconciler, or the response shape. The structural guarantee underpinning US2 is now exercised by both an in-process integration test and a CI-runnable script.

---

## Phase 5: User Story 3 — The backend determines which bank a PDF belongs to without the client having to know (Priority: P2)

**Goal**: Verify end-to-end that auto-detection is content-based, deterministic, isolated against buggy banks, and fails loudly on the empty registry. Most of the resolver-level behavior is already covered by `BankResolverTests` (T022); this phase adds the **end-to-end** evidence through the live API and the startup-failure evidence.

**Independent Test**: All tasks in this phase pass `dotnet test`. The startup test independently verifies SC-007 by constructing a `WebApplicationFactory` that does NOT register any bank.

### End-to-end determinism

- [x] T040 [US3] Create `tests/CardStatement.Api.Tests/DeterminismTests.cs` containing one test that POSTs the BAC sample PDF 10 times serially through the in-process API (with both BAC and Stub registered, reusing the factory from T038) and asserts: every response has `bank.id == "bac"`; every response's serialized JSON body is byte-identical to the first. This is SC-008's automated form. (Depends on T038.)

### Buggy-bank isolation at the endpoint level

- [x] T041 [US3] Create `tests/CardStatement.Api.Tests/Fixtures/AlwaysThrowingDetectorBankProvider.cs` implementing `IBankProvider` with `Info = new("broken", "Always-Throwing Bank")` whose `Detect` throws `InvalidOperationException("simulated bug")` on every input. Co-locate an `AddAlwaysThrowingDetectorBank()` extension.
- [x] T042 [US3] Create `tests/CardStatement.Api.Tests/BrokenBankIsolationTests.cs` using a factory that registers BAC + the always-throwing bank. Tests: posting the BAC sample PDF still returns 200 with `bank.id == "bac"` (SC-006); the captured logger output (use `ITestOutputHelper` + an in-memory `ILoggerProvider`) contains an error-level message naming `broken` and the exception type. (Depends on T013, T029, T041.)

### Empty-registry startup failure

- [x] T043 [US3] Create `tests/CardStatement.Api.Tests/EmptyRegistryStartupTests.cs` using a custom `WebApplicationFactory<Program>` that removes any registered `IBankProvider` (via `services.RemoveAll<IBankProvider>()`) before the host is built. The test asserts that constructing the `WebApplicationFactory.Services` (or calling `GetRequiredService<IBankRegistry>()`) throws `EmptyBankRegistryException`. This is SC-007's automated form. (Depends on T012, T029.)

### Latency sanity check

- [x] T044 [US3] Create `tests/CardStatement.Api.Tests/MultiBankLatencyTests.cs` that measures the median wall-clock time of POSTing the BAC sample PDF through the in-process API (a) with only BAC registered and (b) with BAC + Stub registered. Run 20 iterations per condition (one warm-up batch discarded). Assert `median(bac+stub) <= median(bac_only) * 1.10` (SC-005). Mark the test as `[Trait("Category", "Performance")]` so CI can opt out on slow runners. (Depends on T029, T036.)

**Checkpoint**: All three user stories' acceptance criteria from spec.md are now backed by automated tests. The empty-registry guarantee, the buggy-bank-isolation guarantee, the determinism guarantee, and the +10%-latency guarantee are all exercised through the live API surface, not just the resolver in isolation.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final integration touches, logging verification, and documentation hand-off. Nothing in this phase is on the critical path for the user-visible refactor; they are quality-of-life improvements for future bank authors and operators.

- [x] T045 [P] Run through `specs/002-multi-bank-support/quickstart.md` Section A end-to-end on a clean machine (or a clean `dotnet clean` + fresh `dotnet build`). Confirm every command in the doc works as written. Fix any drift between the doc and the implementation in the doc (not in the code).
- [x] T046 [P] Run through `specs/002-multi-bank-support/quickstart.md` Section B end-to-end by actually adding a throwaway `BancoX` bank exactly as the recipe describes, then revert it via `git restore`. The exercise is the verification — if you have to deviate from the recipe to make it work, edit the recipe. Time the exercise: it should fit in ≤ 15 minutes (the spec's implicit target for SC-003 ergonomics).
- [x] T047 [P] Audit `src/CardStatement.Api/Program.cs` and `src/CardStatement.Api/Endpoints/ExtractEndpoint.cs` against FR-017 / FR-018 (logging policy): confirm raw PDF bytes and full transaction descriptions are still NOT logged at default level; confirm the per-request log line includes the selected bank id; confirm the three detection outcomes (single / ambiguous / none) emit visually distinguishable log lines (single = info, ambiguous = warning, none = info from resolver + the surfaced 422). Adjust phrasing if needed.
- [x] T048 [P] Update `frontend/src/types/api.ts` (if it exists and is hand-written) to include the optional new `bank?: { id: string; displayName: string }` field on the response — typed as optional so the existing UI compiles without change. **Do not** add any UI to render it; that is a separate spec. If `api.ts` is generated from `contracts/openapi.yaml`, regenerate it instead. (This is the *only* permitted frontend edit in this entire spec, and it is purely a type-level addition; if you find yourself editing components, stop — that belongs in a follow-up spec.)
- [x] T049 Update `CreditStatementParser.slnx` only if any new `csproj` was added (none should be — see plan.md Complexity Tracking). Otherwise this is a no-op verification task: confirm the solution file is unchanged.
- [x] T050 Update `README.md` to mention the multi-bank seam in one short paragraph and link to `specs/002-multi-bank-support/quickstart.md#b-add-a-new-bank-in-15-minutes` as the bank-author onboarding doc. No other README content changes.

**Checkpoint**: Refactor complete. The codebase is structurally ready to accept a second concrete bank in a single PR.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No code dependencies. T002 must run against the pre-refactor `main` branch — it is impossible to recapture the SC-002 baseline after Phase 3 lands.
- **Foundational (Phase 2)**: Depends on Setup. **Blocks every user-story phase** — none of US1/US2/US3 can begin until the seam compiles.
- **US1 (Phase 3)**: Depends on Foundational. Is also a soft prerequisite for US2/US3's end-to-end tests: the live API must boot for `WebApplicationFactory`-based tests in US2/US3 to run, and the API does not boot until BAC is registered (the registry refuses empty input).
- **US2 (Phase 4)**: Depends on Foundational + US1 booted. The stub bank is registered alongside BAC, not in place of it.
- **US3 (Phase 5)**: Depends on Foundational + US1 booted. Reuses the stub bank from US2 (T036) — so if US2 and US3 are split across developers, T036 belongs to whoever lands first.
- **Polish (Phase 6)**: Depends on all of the above.

### User Story Dependencies

- **US1 (P1)**: Strict critical path. Nothing else demos without it.
- **US2 (P2)**: Independent of US3. Once US1 lands, US2 can be done in isolation.
- **US3 (P2)**: Independent of US2 *except* for the shared `StubBankProvider` fixture (T036). If both stories are landed in the same PR, sequence is irrelevant. If split across PRs, US2 lands first and the stub fixture comes with it.

### Within Each User Story

- Tests (the foundational ones in Phase 2; the per-story ones in Phases 3–5) are written **with** the production code they cover — they do not need to fail-first because we are refactoring, not green-fielding (the spec explicitly preserves existing behavior; the new tests assert that preservation). Treat the existing `001-pdf-extract-web` suite as the "test that already fails if you break things" — keep it green at every commit boundary.
- For Phase 3 specifically: do T023/T024 (move files) and T025–T027 (provider/detector/DI) in two commits, not one — the project will not compile between them, so split the commits along the boundary where it does compile so bisect stays useful.

### Parallel Opportunities

- **Phase 2** is the biggest parallelization window: T003–T011, T015, T019, T020 are all `[P]` (different files, no inter-task dependencies). One developer can hand off chunks of these to AI agents in parallel.
- **Phase 3**: T032 and T033 are `[P]` against each other after T026 lands; T031 (test moves) is independent of the provider work and can run alongside T025/T026 once T024 is done.
- **Phase 4 / Phase 5**: T036, T037, T041 are `[P]` against each other (different files). The actual test classes T038/T040/T042/T043/T044 each touch a different file and can be parallelized once their fixtures exist.
- **Phase 6**: T045–T048 and T050 are `[P]` (different files / different scopes).

### Critical-Path Sequence (smallest set of must-be-serial tasks)

```text
T001 → T002 → (T003 ‖ T004 ‖ T005 ‖ T006 ‖ T007 ‖ T008 ‖ T009 ‖ T010 ‖ T011)
     → T012 → T013 → T014
     → (T015 → T016 → T017) ‖ T018
     → T023 → T024 → T025 → T026 → T027 → T028 → T029 → T034 → T035
     → T036 → T038 → T039
     → T040 → T042 → T043 → T044
     → T045–T050
```

`‖` denotes parallelizable groups.

---

## Parallel Example: Phase 2 Foundational

```bash
# All of these touch different files and have no dependencies on each other:
Task: "T003 Create src/CardStatement.Core/Banks/BankInfo.cs"
Task: "T004 Create src/CardStatement.Core/Banks/BankDetection.cs"
Task: "T005 Create src/CardStatement.Core/Abstractions/IBankProvider.cs"
Task: "T006 Create src/CardStatement.Core/Abstractions/IBankRegistry.cs"
Task: "T007 Create src/CardStatement.Core/Abstractions/IBankResolver.cs"
Task: "T008 Create src/CardStatement.Core/Banks/BankResolutionResult.cs"
Task: "T009 Create src/CardStatement.Core/Banks/Exceptions/NoBankMatchedException.cs"
Task: "T010 Create src/CardStatement.Core/Banks/Exceptions/EmptyBankRegistryException.cs"
Task: "T011 Create src/CardStatement.Core/Banks/Exceptions/DuplicateBankIdException.cs"
Task: "T015 Create src/CardStatement.Api/Contracts/BankInfoDto.cs"
```

After these complete:

```bash
# Tests that depend on the foundational types can also parallelize:
Task: "T019 Create tests/CardStatement.Tests/Banks/BankInfoTests.cs"
Task: "T020 Create tests/CardStatement.Tests/Banks/BankDetectionTests.cs"
```

T012/T013/T014 are serial (registry → resolver → DI extension); T016/T017 are serial after T015.

---

## Parallel Example: Phase 3 US1 (after T024 lands)

```bash
# Detector and existing-test moves don't conflict:
Task: "T025 Create src/CardStatement.Core/Banks/Bac/BacDetector.cs"
Task: "T031 git mv existing BAC tests into tests/CardStatement.Tests/Banks/Bac/"
```

After T026 (provider) lands:

```bash
Task: "T032 Create BacDetectorTests"
Task: "T033 Create BacBankProviderTests"
```

---

## Implementation Strategy

### MVP First (Phase 1 → Phase 2 → Phase 3 — US1 only)

1. **Phase 1 Setup** — capture the SC-002 baseline before touching anything. *Crucially: do this on `main`, not on the feature branch.*
2. **Phase 2 Foundational** — build the seam in isolation. Project will not run after this phase — that is correct.
3. **Phase 3 US1** — move BAC into the seam, wire DI, prove regression. **This is the MVP.** The refactor is shippable here: behavior is identical, the seam exists for future banks, no second bank is required.
4. **STOP and VALIDATE**: Re-run `dotnet test` and the byte-parity test. If green, this PR is mergeable.

### Incremental Delivery

1. Land Phases 1+2+3 as one PR. This is the smallest mergeable unit and the riskiest one (it touches BAC). It is also the only PR in this spec that ships a behavioral diff (the additive `bank` field).
2. Land Phases 4 (US2) + 5 (US3) as a second PR. This PR is purely additive tests + test fixtures — no production code in `src/` changes. The structural script T039 acts as the merge gate.
3. Land Phase 6 (Polish) as a third PR if it grows beyond doc tweaks; otherwise inline into PR 2.

### Parallel Team Strategy

With multiple developers (after Phase 2 lands):

- **Developer A**: Phase 3 US1 (critical path; touches BAC and DI).
- **Developer B**: Phase 4 US2 (test fixtures, multi-bank routing tests). Blocked on US1 being mergeable, but the stub fixture (T036) and PDF fixture (T037) can be drafted in advance.
- **Developer C**: Phase 5 US3 (determinism, startup-failure, latency tests). Same blocking situation as US2; the always-throwing fixture (T041) can be drafted in advance.

The three developers' work converges at the SC-003 verification (T039), which mechanically prevents US2/US3 work from accidentally editing the BAC tree.

---

## Notes

- `[P]` tasks = different files, no dependencies between them at task-creation time. Within a phase, a `[P]` task may still depend on an earlier non-`[P]` task in the same phase.
- `[Story]` label maps task to a specific user story for traceability. Phase 1/2/6 tasks have no story label.
- Spec's existing-behavior preservation is enforced by leaving `001-pdf-extract-web`'s tests untouched. Treat any change required to make those tests pass as a *bug in this plan*, not as expected churn.
- Commit boundary tip: never leave the repo in a state where `dotnet build` fails on `main`. Phase 3's T023/T024 (file moves + namespace updates) is the one place this is easy to violate; do it as a single commit that compiles end-to-end.
- The frontend stays untouched except for the optional type-only addition in T048. If you find yourself editing a `.tsx` file, stop and re-read FR-009.
