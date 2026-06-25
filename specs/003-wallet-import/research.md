# Research: Wallet Import from BAC PDF Statements

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Date**: 2026-06-25

This file resolves every decision the plan depends on. Each entry is in the form **Decision → Rationale → Alternatives rejected**. Order matches the plan's Phase 0 numbered list.

---

## 1. Where Wallet-integration code lives in the backend

**Decision**: Inside `src/CardStatement.Api/`, in a new `Wallet/` folder paralleling the existing `Endpoints/`, `Contracts/`, `ErrorHandling/`, `Mapping/`. Not in `CardStatement.Core`. Not in a new csproj.

**Rationale**: `CardStatement.Core` is the bank-agnostic, dependency-free statement parser. Coupling it to an outbound budget-tracker integration would break that contract and force the `App` CLI (which doesn't talk to Wallet) to take a transitive dependency on `HttpClient`. The `Api` project, by contrast, is the system's host process; it already owns inbound HTTP, CORS, JSON configuration, and DI registration, so it is the natural place for outbound HTTP. The `Banks/` folder in `Core` is the precedent for the "one folder per external system" pattern; `Wallet/` in `Api` follows it.

**Alternatives rejected**:
- Putting it in `CardStatement.Core` — couples the parser to a budget tracker.
- New `CardStatement.Wallet` csproj — adds a build boundary that protects nothing. Only `Api` will consume it. If a second host needs Wallet later, extract then.

---

## 2. Outbound HTTP shape

**Decision**: A typed `IWalletApiClient` interface with four async methods, implemented by `WalletApiClient : IWalletApiClient` and registered as a named typed-`HttpClient` via `services.AddHttpClient<IWalletApiClient, WalletApiClient>(...)`.

```text
ListAccountsAsync()                                 → IReadOnlyList<WalletAccount>
ListCategoriesAsync()                               → IReadOnlyList<WalletCategory>
ListRecordsAsync(accountId, from, to)               → IReadOnlyList<WalletRecord>  // handles pagination internally
CreateRecordsAsync(IReadOnlyList<CreateRecordRequest>) → IReadOnlyList<CreateRecordOutcome>  // handles chunking internally if ever needed (current callers pre-chunk)
```

**Rationale**: Only 4 endpoints are needed; hand-writing them is a fraction of the cost of adopting a generator over the 6,931-line Wallet OpenAPI doc. The typed client gets `HttpClient` lifetime management for free via `IHttpClientFactory`. Tests inject a fake `HttpMessageHandler` to record requests and return canned responses — no real network in tests.

**Alternatives rejected**:
- Code-gen with NSwag or Kiota — bigger toolchain and a generated client we'd have to keep in sync with the OpenAPI doc on every Wallet release. Hand-written wins on size and surface area.
- Raw `HttpClient` injected into `WalletImportService` — leaks transport concerns (auth header, paging, deserialization) into the orchestrator.

---

## 3. Where the JWT lives + how it's injected

**Decision**: `Wallet:Jwt` in `appsettings.json` (or env var `Wallet__Jwt`), bound to `WalletOptions` via `services.Configure<WalletOptions>(...)`. `WalletApiClient` resolves `IOptions<WalletOptions>` and reads `.Value.Jwt` **per outbound call**, attaching `Authorization: Bearer <jwt>` per request.

**Rationale**:
- `appsettings`/env is the standard .NET secret pattern.
- Reading per-request (rather than baking the header into the `HttpClient` instance) means a restart with a new token is all that's needed — no need for a custom `IOptionsMonitor` snapshot.
- Reading per-request also avoids stale-token bugs if `IHttpClientFactory` pools the underlying handler across the boundary between config loads.

**Alternatives rejected**:
- Hard-coding (obvious).
- `IOptionsMonitor<WalletOptions>` — adds a change-token machinery we don't need; the token doesn't change at runtime.
- Setting `BearerAuth` as a default request header on the typed client — would require recreating the client whenever the option changes, complicating the lifecycle.

---

## 4. Empty / missing JWT policy

**Decision**: The backend starts normally regardless of whether `Wallet:Jwt` is set. The existing `POST /api/statements/extract` is NOT gated. The Wallet endpoints (`GET /api/wallet/accounts`, `GET /api/wallet/categories`, `POST /api/wallet/import/compare`, `POST /api/wallet/import/submit`) check `WalletOptions.Jwt` is non-empty at the top of the handler; if it isn't, they return HTTP 503 with the structured envelope:

```json
{ "error": { "code": "WALLET_NOT_CONFIGURED", "message": "Wallet credentials are not configured. Ask the operator to set Wallet:Jwt." } }
```

**Rationale**: Spec FR-024 is explicit: Wallet config must not be a global startup gate. The single-conditional check is in `WalletApiClient.EnsureConfigured()` (called at the top of each public method), so the policy lives in one place. HTTP 503 because the *server* is in a non-operational state for this resource; the *client* is fine.

**Alternatives rejected**:
- Failing startup if JWT missing — breaks the extract page when the operator hasn't configured Wallet yet, which violates FR-024.
- 500 (caller can't tell it's a config problem).
- 401 (would imply the user is unauthenticated; the user has no credentials in this app — it's the *backend's* credentials that aren't set).
- 412 Precondition Failed (technically defensible but obscure; 503 is more widely understood).

---

## 5. Wallet error taxonomy

**Decision**: A new error envelope `WalletErrorResponse { error: { code, message } }` carries a 4-value `WalletErrorCode` enum:

| Code | HTTP | Trigger |
|---|---|---|
| `WALLET_NOT_CONFIGURED` | 503 | `WalletOptions.Jwt` empty/missing |
| `WALLET_CREDENTIALS_INVALID` | 502 | Wallet API returned 401 or 403 |
| `WALLET_UNAVAILABLE` | 504 | Timeout, DNS failure, connection refused, Wallet 5xx |
| `WALLET_REJECTED` | 502 | Wallet API returned a 4xx other than 401/403 (e.g. 400 invalid parameter, 404 unknown account) |

The existing `ExtractionErrorResponse` envelope and `ErrorCodes` enum (`INVALID_FILE_TYPE`, etc.) are preserved unchanged; they're still used for PDF-stage failures of the Compare endpoint (FR-006).

**Rationale**: Clean separation in the frontend (a `WalletErrorBanner` component handles the 4 Wallet codes with operator-actionable copy; the existing `ErrorBanner` handles extraction codes for the upload step). Honors the spec's assumption that Wallet failures don't pollute the existing extraction taxonomy. HTTP statuses follow the convention that 502/504 indicate an upstream issue.

**Alternatives rejected**:
- Extending `ErrorCodes` with Wallet codes — violates `002`'s additive-only contract on the existing OpenAPI surface and conflates two unrelated failure modes.
- Returning 401 for credentials-invalid — semantically wrong; the *caller* isn't unauthenticated.
- One generic `WALLET_ERROR` code — less actionable for the operator (was the token wrong, the network down, or the payload bad?).

---

## 6. Wallet records pagination

**Decision**: `WalletApiClient.ListRecordsAsync(accountId, from, to)` loops:

```
offset = 0
while true:
  GET /v1/api/records?accountId=<id>&recordDate=gte.<from>&recordDate=lt.<to+1d>&limit=200&offset=<offset>
  accumulate response.records
  if response.nextOffset is null/absent: break
  offset = response.nextOffset
```

Note `recordDate=lt.<to+1d>` because the spec's window is **inclusive** on `to` but Wallet's `lt` is exclusive — adding one day to the `lt` bound gives an inclusive end.

**Rationale**: Spec FR-007 requires complete coverage before the comparison renders. Using the documented max `limit=200` minimizes round trips for a typical window. `nextOffset` absence is Wallet's documented "no more pages" signal.

**Alternatives rejected**:
- Default `limit=30` — needlessly tripled round trips.
- `withTotal=true` — Wallet would do extra counting work we don't use; we don't show a total.
- Computing `to + 1d` vs. using `recordDate=lte` (if supported) — `gte`/`lt` are the documented operators; sticking with them.

---

## 7. Date-window math

**Decision**: Comparison window is `[ min(period.issueDate, period.cutoffDate) - 5d, max(period.issueDate, period.cutoffDate) + 5d ]`. For BAC SV statements `issueDate ≤ cutoffDate` always, so this is `[issueDate - 5d, cutoffDate + 5d]`.

**Rationale**: Spec FR-007 says ±5 days at each boundary; unioning over both boundaries closes the gap in the middle. The `min`/`max` formulation is defensive: if a future bank ever issues a statement whose `issueDate > cutoffDate`, the code still does the right thing.

**Edge case**: If `period.issueDate` or `period.cutoffDate` is the .NET default (`DateOnly.MinValue`), treat it as "missing" and surface FR-007's edge case (the user is asked to confirm the range manually). For BAC this case does not occur — the metadata extractor guarantees both dates — but the safety net stays.

**Alternatives rejected**:
- ±5 only at the cutoff — would miss early-period manual entries.
- A fixed 90-day window — silent fallback that hides the missing-dates case.

---

## 8. Duplicate rule encoding

**Decision**: A pure static method `DuplicateMatcher.Match(IReadOnlyList<PdfRowInternal> pdfRows, IReadOnlyList<WalletRecordInternal> walletRecords) → IReadOnlyDictionary<int, IReadOnlyList<string>>` where the key is `pdfRow.Index` and the value is the **sorted** list of matched Wallet record ids.

Match rule per the spec (FR-009):

```
isMatch(pdf, w) :=
    Math.Abs((pdf.Date.DayNumber - w.RecordDate.DayNumber)) <= 2
    && pdf.SignedAmount == w.SignedAmount         // decimal equality
    && pdf.Currency == w.Currency                  // exact string match (already normalized upper)
    && Math.Sign(pdf.SignedAmount) == Math.Sign(w.SignedAmount)   // same direction; both nonzero by construction
```

Determinism contract: outputs are stable across calls — pdfRow indices sorted ascending, matched wallet ids sorted lexicographically inside each list. Test asserts byte-equality across two consecutive runs.

**Rationale**: Reading like the spec FR-009 line-for-line, pure & easily table-tested, and the determinism contract (FR-025) is enforced at the matcher's output boundary so the rest of the pipeline doesn't have to think about it.

**Alternatives rejected**:
- Any "fuzzy" amount tolerance — spec is explicit about exact equality.
- A cross-currency dedup attempt — out of scope (requires FX rates).
- Using direction enums instead of `Math.Sign` — extra plumbing for no behavioral gain since the wire has already projected to signed decimals.

---

## 9. Per-row currency on PDF side

**Decision**: Every PDF row's `currency` in the compare response equals the chosen Wallet account's `currencyCode`. The `currencyMismatch` boolean on `PdfRow` is therefore always `false` in this iteration.

**Rationale**: A `grep -i currency` over `src/CardStatement.Core/` returns no matches — the existing BAC parser has no per-row currency. BAC SV's credit-card statements are typically single-currency (USD-only or SVC-only for legacy cards). Defaulting to the account currency is correct in practice and keeps the contract additive-friendly: when a future spec adds per-row currency extraction to `BacBankProvider`, the `PdfRow.currency` and `currencyMismatch` fields are already in the response shape, so the frontend and downstream consumers absorb the change without contract churn.

**Alternatives rejected**:
- Omitting the field — forces a contract change later.
- Adding per-row currency extraction to BAC in this spec — out of scope (a `CardStatement.Core` change that warrants its own spec).
- Letting the frontend ask the user to pick a per-row currency — out of the spec's UX surface.

---

## 10. PDF signed amount

**Decision**: `signedAmount = direction == Expense ? -amount : +amount`, computed once in `WalletImportService.ToPdfRowDto(...)` before handing rows to the matcher and the frontend. Wallet API signs the same way (negative = expense), so signed `decimal == decimal` compares correctly across both sides.

**Rationale**: Signing once at the boundary keeps every downstream consumer (the matcher, the frontend, the submit translation back into Wallet's `amount.value`) in a single sign convention. The submit translation reverses: `amount.value = row.signedAmount` (already signed).

**Alternatives rejected**:
- Passing `(amount, direction)` through the pipeline — two parallel ways to express the same information.

---

## 11. Default selection rule

**Decision**: Backend computes `defaultSelected = pairings[index].Count == 0 && !currencyMismatch` and emits it on each `PdfRow`. Frontend renders it; it does not re-derive.

**Rationale**: Spec FR-014 ("likely duplicates default unselected") plus FR-010 ("currency mismatches default unselected") combine into one rule. Computing it server-side puts it next to the matcher that produced the pairings, so the policy lives in one place. The test asserting "for the BAC sample + this fake Wallet snapshot, exactly these row indices default selected" is then a deterministic backend test.

**Alternatives rejected**:
- Deriving the rule in the frontend — duplicates logic and makes regression testing harder.

---

## 12. Where the cardholder→label mapping is read

**Decision**: `WalletOptions.LabelMapping : Dictionary<string, string>` (key: `RawName`, value: Wallet `labelId`). Bound from `appsettings.json` (or env vars: `Wallet__LabelMapping__FERNANDO MAGAÑA=…` — note that env-var keys with spaces require the operator to use a `.env` file or quoted env-var assignment; the standard `appsettings.json` route is recommended). Lookups are case-insensitive: the resolver builds an internal `Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)` once per request from the options snapshot.

A row whose `cardholderSectionRawName` has no entry yields `labelIds: []`. Compare response carries `unmappedSections: string[]` — the **distinct** set of cardholder section names from the PDF that have no mapping — so the frontend can surface "Cardholders without a mapping: X, Y" once at the top instead of decorating every row.

**Rationale**: Spec assumption says unmapped is allowed (not an error) but must be visible. The `unmappedSections` summary is cheaper UX than per-row badges. Case-insensitive lookup hedges against operator transcription drift; PDF names are uppercase ASCII-with-accents but operators may type them in any case.

**Alternatives rejected**:
- Per-row `unmapped: bool` flag — noisier in the comparison table; the user mostly cares "is *any* of my data losing labels?" once.
- Throwing on unmapped — violates spec.

---

## 13. Submit batching + per-row outcome

**Decision**:

```text
WalletImportService.SubmitAsync(req):
  if Wallet not configured → throw WalletApiException(NotConfigured)
  resolved = req.rows.Select(toCreateRecordRequest)        // applies LabelMapping per row, paymentType=credit_card, accountId
  for chunk in resolved.Chunk(50):                          // preserves order
      response = walletClient.CreateRecordsAsync(chunk)     // throws WalletApiException on 401/403/network/5xx
      outcomes.AddRange(toSubmitOutcomes(response))
  return SubmitResponse(outcomes)
```

On `WalletApiException(CredentialsInvalid)` thrown from any chunk, the orchestrator stops iterating and the endpoint converts the exception into a top-level `WalletErrorResponse(WALLET_CREDENTIALS_INVALID)` envelope (HTTP 502). The frontend renders this as a single banner with the row selections preserved, per FR-020.

On `WalletApiException(Unavailable)` or `(Rejected)` from a mid-batch chunk, the orchestrator currently also stops and reports the corresponding top-level error code (rows from earlier chunks have already succeeded and remain in Wallet — the spec accepts non-atomicity). This is a deliberate trade-off; the alternative (continuing past a network failure) would mean every subsequent chunk gets the same error and the user sees N copies. Stopping after the first non-401/403 upstream failure is one error to read.

**Rationale**: Matches the spec exactly on 401/403 → one banner (FR-020). For the other Wallet exception kinds the spec is silent on mid-batch behavior; failing-fast is the more honest choice.

**Alternatives rejected**:
- Stopping on the first per-row failure — regression of FR-019 ("a failure on one row MUST NOT prevent other rows from being created").
- Per-chunk retries — out of scope; spec asks for a single attempt + user-driven retry.

---

## 14. Where extraction happens for the Compare endpoint

**Decision**: A shared `PdfUploadGuard` helper (size + magic-bytes + empty checks) used by both `ExtractEndpoint` and `WalletImportEndpoint`. Inside the Compare handler, after guards pass, the same `IPdfExtractor` + `IBankResolver` pair the existing endpoint uses is invoked. `WalletImportService.CompareAsync(pdfWords, accountId, account, categories, wallet records, label mapping)` is then pure.

**Rationale**: Spec FR-005 explicitly demands one source of truth for "what's in this PDF". Reusing the same services makes the guarantee structural rather than aspirational. Extracting the file-guard rules into a helper also tightens `ExtractEndpoint` by ~20 lines.

**Alternatives rejected**:
- Frontend uploads the PDF twice (once to extract, once to compare) — two round trips with the same file; gross.
- Cache by hash on the backend between Extract and Compare — session state, against the stateless constraint.

---

## 15. Frontend routing choice

**Decision**: `react-router-dom@^7` (currently 7.10 at time of writing).

**Rationale**: Tiny dep, works out-of-the-box with React 19 + Vite 8, ubiquitous and well-understood, lets us add a second route in one line.

**Alternatives rejected**:
- Hand-rolled `useState`-based "screen" switcher — cheap today, costs more the moment a third page is added.
- TanStack Router — more powerful than this feature needs.

---

## 16. Frontend data-fetching style

**Decision**: Each page component fetches in `useEffect` and manages its own loading/error state via a local `useReducer` — matching the existing `App.tsx` pattern.

**Rationale**: Zero new mental model for engineers working on the repo. The existing extract page already works this way; copying the idiom keeps the codebase coherent.

**Alternatives rejected**:
- `react-router` loaders — would force restructuring routes around data; the smallest-step approach prefers staying close to the existing pattern.
- TanStack Query / SWR — caching machinery we don't need for a single-shot fetch per page visit.

---

## 17. Where the existing extract page goes

**Decision**: Move the existing single-page contents out of `App.tsx` into `frontend/src/pages/StatementExtractPage.tsx` verbatim. `App.tsx` shrinks to:

```tsx
<BrowserRouter>
  <Nav />
  <Routes>
    <Route path="/" element={<StatementExtractPage />} />
    <Route path="/wallet-import" element={<WalletImportPage />} />
  </Routes>
</BrowserRouter>
```

**Rationale**: Cheapest split; preserves the existing extract behavior without semantic change; only `main.tsx` and `App.tsx` are edited among existing files. The existing components under `frontend/src/components/` are reused by the moved page without modification.

**Alternatives rejected**:
- Leaving extract content in `App.tsx` and bolting the new page in elsewhere — asymmetric and confusing.

---

## Open questions resolved by user-supplied answers

The four scoping questions answered before the spec was written are reproduced here so a future reviewer doesn't have to chase the spec's input header:

| Question | User answer |
|---|---|
| Where does the Wallet JWT live? | Backend reads from local config/env. |
| Where do Wallet API calls go out from? | Through the existing .NET backend (proxy). |
| How are `accountId`/`categoryId`/`labelIds`/`paymentType` set on imported records? | Account picked once per PDF; category picked per row; labels mapped from cardholder sections via `appsettings`; `paymentType` fixed at `credit_card`. |
| What's the UI shape? | New route/page for Wallet Import. |

No `[NEEDS CLARIFICATION]` markers remain in the spec, and none surfaced during planning.
