# Feature Specification: Multi-Bank Backend Support

**Feature Branch**: `002-multi-bank-support`
**Created**: 2026-05-29
**Status**: Draft
**Input**: User description: "Create a tool for a fully technical spec to enhance this application to support different types of PDFs associated to other banks. The scope of this spec is just to refactor the backend to support adding new banks in the future. Currently we will only support the current bank BAC, but the project should be able to extend to other banks in the future."

## User Scenarios & Testing *(mandatory)*

This iteration is a **backend-only refactor**: the existing extraction surface (one HTTP endpoint that accepts a PDF and returns extracted transactions) is reshaped internally so that the bank whose statement is being parsed becomes a first-class, pluggable concept. After this refactor the system still ships **exactly one** working bank — BAC Credomatic — with byte-identical output to today. The value is structural: adding a second bank later becomes a localized, additive change instead of forking the parser. There is no new user-visible workflow in this spec; the "users" of the refactored seam are future contributors who will add banks, plus the existing frontend, whose contract MUST keep working unchanged.

### User Story 1 - Existing BAC extraction continues to work unchanged (Priority: P1)

A user of the existing web app uploads a BAC Credomatic statement PDF (the same `samples/final5140_45178439_316493_0.pdf` used in `001-pdf-extract-web`), and receives an extraction response that is **functionally identical** to the response produced by the system before this refactor: same statement header, same cardholder sections, same row count, same per-row values, same per-section subtotals, same printed/computed totals, same reconciliation status, and the same set of structured error codes for failure cases.

**Why this priority**: A refactor whose first effect is to break the only working bank is worse than no refactor. This story is the regression gate — every other story in this spec is gated on it holding true. It also defines what "the BAC parser" means once the refactor lands: not "the code path that runs by default", but "one named, registered bank implementation among potentially many".

**Independent Test**: Run the existing `001-pdf-extract-web` end-to-end test suite (backend integration tests against the sample PDF, plus the existing frontend integration test that hits the live API) without changes. Every test passes. The JSON returned for the sample PDF is byte-for-byte identical to the pre-refactor response, modulo any deliberate additive fields documented in this spec (e.g. a `bankId` echo — see FR-009).

**Acceptance Scenarios**:

1. **Given** the refactored backend is running and the existing frontend is unchanged, **When** the user uploads the sample BAC PDF, **Then** the response contains the same statement header, the same number of cardholder sections in the same order, the same number of transactions per section in the same order, and the same per-row field values as the pre-refactor baseline captured for that PDF.
2. **Given** the refactored backend is running, **When** an existing integration test from `001-pdf-extract-web` is run against it, **Then** every assertion passes without modification (the test code does not have to learn about banks to keep working).
3. **Given** the refactored backend is running, **When** the user uploads each of the existing error fixtures (non-PDF, scanned-only PDF, unrecognized layout, password-protected, file too large), **Then** each one returns the same structured error code and HTTP status as before the refactor.

---

### User Story 2 - A new bank can be added without modifying existing bank code (Priority: P2)

A developer wants to add support for a second bank (call it Bank X). They create a new bank implementation that supplies (a) the bank's identity, (b) a way to recognize that a given PDF belongs to this bank, and (c) the bank-specific parsing logic that turns the PDF's text into the same shared transaction/section/statement shape the rest of the system already speaks. They register the new bank with the backend. They do **not** touch the BAC implementation, the shared statement model, the HTTP endpoint code, the reconciliation logic, or the error-mapping code.

**Why this priority**: This is the entire reason the refactor exists. If adding a bank requires editing the BAC parser, the endpoint, or shared core code, the refactor failed even if the BAC flow still works. P2 (not P1) because the BAC regression gate must hold first — but every code-shape decision in this spec exists to make this story cheap.

**Independent Test**: A throwaway "stub bank" implementation (e.g. one that recognizes a PDF whose text contains a magic marker `__TEST_BANK__` and returns a hard-coded single-section statement) can be added to the backend by adding **only new files** (one bank implementation file plus its registration) — no edits to BAC code, no edits to shared models, no edits to the endpoint. The stub bank is invoked end-to-end for a PDF that matches its marker, and the BAC parser remains invoked for the existing sample PDF in the same test run.

**Acceptance Scenarios**:

1. **Given** the codebase has only BAC registered, **When** a developer adds a new bank by introducing a single new bank implementation and registering it once, **Then** the new bank participates in extraction requests for PDFs it recognizes, with zero edits to BAC code, the shared statement model, the HTTP endpoint, the reconciliation engine, the error mapping, or the response DTOs.
2. **Given** a second bank is registered alongside BAC, **When** a PDF matching the second bank is uploaded, **Then** the second bank's parser is invoked and the BAC parser is not, and the response shape is the same shared shape the frontend already understands.
3. **Given** a second bank is registered alongside BAC, **When** a PDF matching BAC is uploaded, **Then** the BAC parser is invoked and the second bank's parser is not, and the response is identical to the pre-refactor BAC output (regression gate from US1 still holds).
4. **Given** a second bank's parser throws an unexpected exception during parse, **When** the request completes, **Then** the failure is mapped to the same structured error envelope that BAC failures use today (no bank can poison the endpoint's contract).

---

### User Story 3 - The backend determines which bank a PDF belongs to without the client having to know (Priority: P2)

A user of the web app uploads a PDF without telling the system which bank issued it (the existing upload UI has no bank picker, and this spec does not add one). The backend inspects the PDF and selects the correct registered bank. If no registered bank recognizes the PDF, the backend returns the existing "unrecognized layout" structured error rather than guessing, silently emitting an empty result, or invoking the wrong parser.

**Why this priority**: Same priority as US2 because the two together define the contract. Without bank detection the multi-bank seam is theoretical — the endpoint would either have to break its existing contract (require the client to send a bank id) or always fall back to BAC. With detection, US2's "add a bank by adding a file" claim becomes real end-to-end.

**Independent Test**: With BAC plus the stub bank from US2 both registered: (a) uploading the sample BAC PDF routes to BAC; (b) uploading the stub-marker PDF routes to the stub; (c) uploading a PDF neither bank recognizes (e.g. a blank PDF or a PDF from a third unregistered bank) returns the existing `UNRECOGNIZED_LAYOUT` error code with no parser being invoked beyond detection. None of these three tests requires the client to send a bank hint.

**Acceptance Scenarios**:

1. **Given** multiple banks are registered, **When** a PDF is uploaded, **Then** exactly one bank is selected to parse it, chosen by inspecting the PDF content, not by client input or by registration order alone.
2. **Given** no registered bank recognizes the uploaded PDF, **When** detection completes, **Then** the response is the existing `UNRECOGNIZED_LAYOUT` structured error (same code, same HTTP status, same envelope) and no bank-specific parser is invoked.
3. **Given** more than one registered bank claims the same PDF (an unintended ambiguity), **When** detection completes, **Then** the system produces a deterministic, repeatable outcome (the same bank is chosen for the same input every time) and surfaces the ambiguity in server logs so the conflict can be diagnosed and fixed by the bank authors.
4. **Given** the same PDF is uploaded twice in the same session, **When** each request completes, **Then** the same bank is selected both times and the extracted output is byte-identical (determinism extends to detection, not just parsing).

### Edge Cases

- **Detection is slow or expensive on large PDFs**: detection MUST run on already-extracted text (or a bounded slice of it), not by re-parsing the PDF from scratch per registered bank; adding banks MUST NOT cause request latency to grow linearly with the number of registered banks beyond a small constant overhead.
- **A bank's detection logic throws** (e.g. malformed regex on edge-case text): the error is contained to that bank — detection treats it as "this bank does not claim this PDF" and continues evaluating other banks, while logging the bug for the bank's author. One buggy bank MUST NOT take down the endpoint.
- **A bank's parser throws after it claimed the PDF**: surfaces as the existing `PARSE_FAILED` (or, if it's a recognized layout class, `UNRECOGNIZED_LAYOUT`) structured error, never as a 500 leaking the exception type or stack.
- **Zero banks registered**: starting the backend with no registered banks is a misconfiguration; the backend MUST fail loudly at startup rather than accept requests and answer every one with `UNRECOGNIZED_LAYOUT`. This protects against silent regressions where a refactor accidentally removes the BAC registration.
- **A bank that recognizes nothing is added**: registering a bank whose detector never claims any PDF is permitted (it is structurally valid; it just never gets work). Detection MUST still complete normally.
- **Identifying a bank without identifying a layout version**: a bank may have multiple statement layouts over time (e.g. format changes in 2027). The seam this spec introduces is at the *bank* level, not the *layout* level; layout variation within a single bank is the bank implementation's internal concern, not a separate registration. This keeps the registry small and makes the common case (one bank, one current layout) cheap.
- **Concurrent uploads of PDFs from different banks**: each request resolves its own bank independently; there is no cross-request state, and registered bank implementations MUST be safe to invoke concurrently.
- **A PDF that is technically parseable by Bank A but was actually issued by Bank B**: this is the ambiguity case from US3 acceptance #3 — the resolution is deterministic and logged; resolving the underlying false-positive is the responsibility of the bank that misidentified.

## Requirements *(mandatory)*

### Functional Requirements

**Bank as a first-class concept**

- **FR-001**: The backend MUST treat "a bank" as a named, registered unit that owns (a) a stable bank identifier, (b) a human-readable display name, (c) a way to decide whether a given uploaded PDF belongs to it, and (d) the parsing logic that turns that PDF into the existing shared statement shape (sections, transactions, printed totals).
- **FR-002**: The backend MUST allow new banks to be registered alongside existing ones without modifying the code of any already-registered bank, without modifying the shared statement model, and without modifying the HTTP endpoint that serves extraction requests.
- **FR-003**: The backend MUST ship with exactly one registered bank in this iteration — BAC Credomatic (El Salvador) — and its behavior MUST be functionally identical to the BAC behavior shipped in `001-pdf-extract-web` (same extracted rows for the sample PDF, same error codes for the existing error fixtures).

**Bank detection / routing**

- **FR-004**: For each upload, the backend MUST select exactly one registered bank to handle parsing, by consulting each registered bank's recognition logic against the already-extracted PDF text (not by client input).
- **FR-005**: If no registered bank claims an uploaded PDF, the backend MUST respond with the same `UNRECOGNIZED_LAYOUT` structured error (same code, same HTTP status, same envelope shape) that the single-bank system uses today for unrecognized layouts.
- **FR-006**: If more than one registered bank claims the same PDF, the backend MUST produce a deterministic, repeatable selection (same PDF ⇒ same bank, every time, regardless of process restart or registration order changes) and MUST log the ambiguity at warning level with enough detail (the two or more bank identifiers, the PDF's filename) to let the bank authors diagnose and fix the conflict.
- **FR-007**: Bank detection MUST NOT re-extract or re-open the PDF per registered bank; PDF text extraction happens once per request, and all registered banks evaluate the same already-extracted input.
- **FR-008**: A bank's detection logic that throws an exception MUST be treated as "this bank does not claim this PDF" — detection continues with the remaining banks, and the exception is logged with the offending bank's identifier so its author can fix it. A single misbehaving bank MUST NOT cause the request to fail.

**Endpoint contract preservation**

- **FR-009**: The existing extraction endpoint MUST keep its existing HTTP path, method, request shape (single PDF file upload), success response shape, and error envelope shape. The response MAY additively include the selected bank's identifier and display name so the frontend can show "extracted from BAC Credomatic"; adding this field MUST be additive only and MUST NOT change or remove any existing field.
- **FR-010**: The full set of structured error codes available today (`INVALID_FILE_TYPE`, `FILE_TOO_LARGE`, `EMPTY_FILE`, `PASSWORD_PROTECTED`, `NO_TEXT_EXTRACTABLE`, `UNRECOGNIZED_LAYOUT`, `PARSE_FAILED`) MUST remain available with their existing codes and HTTP status mappings.
- **FR-011**: A bank's parser throwing an unexpected exception MUST be mapped to the existing structured error envelope (either `UNRECOGNIZED_LAYOUT` if the failure is a recognized layout-mismatch class, or `PARSE_FAILED` otherwise). The endpoint MUST NOT leak exception types, stack traces, or bank-internal error shapes to the client.

**Determinism, isolation, and safety**

- **FR-012**: Extraction MUST remain deterministic end-to-end: the same uploaded PDF MUST produce the same selected bank and the same extracted output on every request, across process restarts, regardless of how many other banks are registered.
- **FR-013**: Registered bank implementations MUST be safe to invoke concurrently from multiple in-flight requests; the backend MUST NOT serialize requests on bank state.
- **FR-014**: No registered bank MUST be able to mutate the shared statement model, the registry of other banks, the response DTO shape, or the endpoint's contract. The seam is one-way: the bank produces a shared statement; it does not reach back into the system.
- **FR-015**: Starting the backend with zero registered banks MUST fail loudly at startup (the process does not come up, or comes up in a clearly-broken state with a visible startup error). Silently accepting requests with no banks registered is forbidden, because it would make every request indistinguishable from "unrecognized layout".

**Operability**

- **FR-016**: At startup, the backend MUST log the list of registered bank identifiers (and display names) at information level so operators can confirm which banks are active.
- **FR-017**: For each handled request, the backend MUST log the selected bank's identifier (or the fact that no bank was selected) alongside the existing per-request log fields, without logging raw PDF bytes or full transaction descriptions (the existing logging-privacy constraint from `001-pdf-extract-web` is preserved).
- **FR-018**: Logs MUST distinguish the three detection outcomes — exactly one bank claimed, multiple banks claimed (ambiguity), zero banks claimed (unrecognized layout) — so the operator can tell at a glance whether a `UNRECOGNIZED_LAYOUT` error means "really no bank knows this PDF" vs. "the right bank's detector has a bug".

**Out of scope for this iteration (do not implement)**

- Any new bank besides BAC Credomatic — adding a second concrete bank is the next spec.
- Any frontend change. The frontend MUST keep working unchanged against the refactored backend. A bank picker, a bank-aware upload UI, or any per-bank display logic in the UI are explicitly deferred.
- LLM-based categorization, label resolution, persistence, authentication, multi-user accounts, or any other surface deferred by `001-pdf-extract-web`.
- Hot-reloading or runtime registration of banks. Banks are registered at startup; adding a bank is a code change plus a rebuild/restart.
- A dynamic plugin discovery mechanism (e.g. scanning a directory for assemblies at runtime). Registration is explicit and code-driven.
- A bank-administration UI, a per-bank metrics dashboard, or any tooling around the registry beyond startup and per-request logs.
- Versioning a single bank's statement layout as a separately-registered entity. Layout variation within one bank is the bank implementation's internal problem.
- Changing the response shape in any non-additive way. Renaming fields, restructuring sections, or moving totals are out of scope here.

### Key Entities

- **Bank**: a named, registered participant in the extraction pipeline. Has a stable identifier (machine-readable), a display name (human-readable), recognition logic (decides whether a given extracted-PDF-text belongs to this bank), and parsing logic (turns that text into the shared statement shape). Registered at backend startup. Adding a bank is additive: it does not alter any other bank, the shared statement model, the endpoint contract, or the registry's behavior beyond making one more entry available.
- **Bank Registry**: the collection of registered banks the backend knows about. Iterated once per request to perform detection. Its membership is fixed for the lifetime of the process. Starting with an empty registry is a startup-time failure.
- **Bank Detection Result**: the outcome of asking every registered bank whether it claims an uploaded PDF. Exactly one of: *single match* (one bank claimed it — that bank parses), *no match* (zero banks claimed it — return `UNRECOGNIZED_LAYOUT`), or *ambiguous match* (more than one bank claimed it — resolved deterministically, logged as a warning, then proceeds as *single match*).
- **Extracted Statement (response root)**: the existing shared shape — statement header, ordered cardholder sections, computed totals, printed totals, reconciliation status — with one additive field identifying which bank produced it (identifier plus display name). All existing fields keep their existing shapes.
- **Extraction Error**: the existing structured error envelope — same code set, same HTTP statuses. Unchanged.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After the refactor, **100%** of the existing `001-pdf-extract-web` backend tests pass without modification, including the sample-PDF integration test that asserts exact extracted rows.
- **SC-002**: For the sample BAC PDF, the extracted statement JSON returned by the refactored backend matches the pre-refactor JSON **byte-for-byte** on every field that existed before this spec. The only permitted difference is the additive bank-identity field (FR-009).
- **SC-003**: Adding a second bank end-to-end (recognize a marker PDF, parse it to a valid shared statement, route to it for matching PDFs and not for BAC PDFs) is achievable by adding **only new files** — zero edits to existing BAC code, zero edits to the shared statement model, zero edits to the HTTP endpoint, zero edits to the reconciliation engine, zero edits to the error mapping, and zero edits to the response DTO. Measured by running `git diff --stat` against `main` after the change and confirming the changed-file list contains only additions plus exactly one registration-point edit.
- **SC-004**: For each of BAC, a registered stub second bank, and a PDF claimed by neither, exactly the expected detection outcome occurs (BAC routes to BAC; stub routes to stub; unclaimed returns `UNRECOGNIZED_LAYOUT`) — verified by automated tests with all three banks active simultaneously.
- **SC-005**: Adding a second registered bank does **not** measurably increase per-request extraction latency beyond a small constant overhead — specifically, the sample BAC PDF's end-to-end extraction time stays within **+10%** of the single-bank baseline when a second registered bank is active (verifies FR-007: detection does not re-extract the PDF per bank).
- **SC-006**: A registered bank whose detector throws on every input does **not** prevent BAC from handling the BAC sample PDF in the same test run — verified by an automated test that registers a deliberately-broken bank alongside BAC and asserts the BAC sample still extracts successfully.
- **SC-007**: Starting the backend with no registered banks fails loudly — the process does not enter a state where it accepts HTTP requests and returns `UNRECOGNIZED_LAYOUT` for every one. Verified by a startup test that asserts the failure.
- **SC-008**: Uploading the same PDF twice (or 100 times) yields the same selected bank on every request, regardless of which other banks happen to be registered alongside it — verified by a determinism test that registers BAC plus a stub and uploads the sample PDF repeatedly.

## Assumptions

- **The shared statement model is already the right shape across banks.** The model produced by today's BAC parser (statement header → cardholder sections → transactions, plus per-section subtotals and statement totals) is general enough to represent statements from other credit-card-issuing banks without structural change. If a future bank's statement does not fit (e.g. no cardholder sections, or fundamentally different total shapes), that is a separate, future spec to evolve the shared model — not in scope here.
- **The existing PDF text-extraction layer is bank-agnostic.** The mechanism that turns PDF bytes into positioned words (currently `IPdfExtractor` / `PdfPigExtractor`) is reused across all banks; only the *parsing* of those words is per-bank. Banks whose PDFs require a fundamentally different text-extraction approach (e.g. OCR for scanned PDFs) are out of scope here.
- **Banks identify their own PDFs from the extracted text.** A bank's recognition logic is allowed to inspect the already-extracted words (text, page count, possibly positions if needed) but does not re-open the PDF. This is the trade-off behind FR-007's latency guarantee.
- **Registration is code-driven and startup-time.** Banks are registered in code at backend startup, not via runtime discovery, config files, or hot-reload. This is the minimum needed to ship multi-bank support without inventing a plugin loader.
- **The existing endpoint, error envelope, and DTO names are the contract.** The frontend, the integration tests, and any external caller depend on these. This spec deliberately constrains itself to additive changes (FR-009).
- **Logging-privacy constraints from `001-pdf-extract-web` carry forward.** Raw PDF bytes and full transaction descriptions remain unloggable at default log level; per-bank logs add identifiers and detection outcomes, not PDF content.
- **The existing reconciliation engine is bank-agnostic.** Once a bank has produced a shared statement (computed-from-rows totals and printed-on-PDF totals), the reconciler that compares them does not need to know which bank it came from. Banks needing bank-specific reconciliation tolerances or rules are a future evolution, not this spec.
- **The "frontend keeps working unchanged" guarantee is what makes this a true backend-only refactor.** If a change requires touching the frontend, it belongs in a follow-up spec — not here. The additive bank-identity field is the one thing the frontend *may* later choose to display, but it does not have to.
