# Quickstart: Wallet Import from BAC PDF Statements

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Date**: 2026-06-25

This is the operator/developer onboarding doc for the Wallet Import feature. It covers (a) configuration, (b) running both processes locally, (c) walking the full Compare → Submit flow, and (d) running the test suite without a real Wallet token.

---

## 0. Prerequisites

- .NET 10 SDK (`dotnet --version` ≥ `10.0.201`, matches `global.json`).
- Node.js 20+ and pnpm (`frontend/pnpm-lock.yaml`).
- A Wallet API JWT (Bearer token) for an account you control. Get one from the Wallet provider's API page. The token's `sub` claim identifies your client.

> **Without a Wallet JWT** you can still: run the existing `/api/statements/extract` page; run the full backend test suite (uses fake handlers); run the frontend test suite. You cannot run the live Compare/Submit flow against a real Wallet account.

---

## 1. Configure the Wallet integration

Pick **one** of the two channels below. `appsettings.json` is recommended for local development; env vars for CI / containers.

### 1a. `appsettings.json` (recommended)

Edit `src/CardStatement.Api/appsettings.json` (or, better, drop the same block into `src/CardStatement.Api/appsettings.Development.json` so secrets stay out of source control):

```json
{
  "Wallet": {
    "BaseUrl": "https://api.wallet.example.com",
    "Jwt": "eyJhbGciOiJI...",
    "TimeoutSeconds": 30,
    "LabelMapping": {
      "FERNANDO MAGAÑA":  "lbl_main_cardholder_xxx",
      "MARIA GARCÍA":     "lbl_secondary_cardholder_xxx"
    }
  }
}
```

- `BaseUrl` is Wallet's public base URL (without trailing slash). The backend appends `/v1/api/...` to it.
- `Jwt` is your Bearer token, raw (no `Bearer ` prefix).
- `TimeoutSeconds` defaults to 30 if omitted.
- `LabelMapping` keys are the BAC PDF cardholder section names (`RawName`, typically uppercase with accents preserved). Values are Wallet label ids. Lookups are case-insensitive.

> 💡 **Get the label ids**: from the Wallet UI, open a record that already has the desired label, click into the label, and copy the id from the URL or the API response — or run `GET /v1/api/labels` against your token with `curl`.

### 1b. Environment variables

```bash
export Wallet__BaseUrl='https://api.wallet.example.com'
export Wallet__Jwt='eyJhbGciOiJI...'
export Wallet__TimeoutSeconds=30
# LabelMapping with names containing spaces is awkward in env vars;
# use appsettings.Development.json instead.
```

### 1c. Leave it unconfigured

If you skip the `Wallet` section entirely, the backend still starts. The existing `/api/statements/extract` keeps working. Any request to a `/api/wallet/*` endpoint returns HTTP 503 with:

```json
{ "error": { "code": "WALLET_NOT_CONFIGURED", "message": "Wallet credentials are not configured. Ask the operator to set Wallet:Jwt." } }
```

The frontend's Wallet Import page surfaces this verbatim as an operator-actionable banner.

---

## 2. Run the backend

```bash
cd src/CardStatement.Api
dotnet run
```

Expected boot log lines:

```
info: Registered banks: bac (BAC Credomatic (El Salvador))
info: Now listening on: http://localhost:5080
```

Smoke test the existing extract endpoint (unchanged from `001`/`002`):

```bash
curl -X POST -F 'file=@../../samples/final5140_45178439_316493_0.pdf' \
     http://localhost:5080/api/statements/extract \
     | jq '.bank, .statement.period'
```

Smoke test the new Wallet accounts endpoint (requires a configured JWT):

```bash
curl -s http://localhost:5080/api/wallet/accounts | jq '.accounts | length'
```

If the JWT is missing or invalid you'll get a `WALLET_NOT_CONFIGURED` or `WALLET_CREDENTIALS_INVALID` envelope instead — the frontend handles both.

---

## 3. Run the frontend

```bash
cd frontend
pnpm install        # first time, picks up the new react-router-dom dep
pnpm dev
```

Opens `http://localhost:5173`. The top nav now shows two links:

- **Statement Extract** (`/`) — the existing single-PDF extract page, unchanged.
- **Wallet Import** (`/wallet-import`) — the new flow.

---

## 4. Walk the full Wallet Import flow

1. Click **Wallet Import** in the top nav (or navigate directly to `http://localhost:5173/wallet-import`).
2. Wait for the **account dropdown** to populate. (One round trip to `/api/wallet/accounts`.) If you see the Wallet error banner, fix the configuration and refresh.
3. Pick the Wallet account whose records correspond to the BAC card on the statement. The upload control unlocks.
4. Drop or select the BAC PDF (e.g. `samples/final5140_45178439_316493_0.pdf`). Click **Upload**.
5. The backend now does, in one round trip to `/api/wallet/import/compare`:
   - PDF safety guards + extraction (same code path as the existing extract page).
   - Window computation (`issueDate - 5d` … `cutoffDate + 5d`).
   - Paginated `GET /v1/api/records` for the chosen account in the window.
   - Categories fetch (`GET /v1/api/categories`).
   - Duplicate-match per row.
6. The **comparison view** appears: PDF rows on the left, Wallet rows on the right, pairings drawn between them. Rows that match an existing Wallet record are **unselected** by default; rows with no match are **selected**.
7. For each row you want to import, pick a **Category** from its dropdown. (Submit is disabled until every selected row has a category.) The per-row preview shows you exactly what will be POSTed: account, date, signed amount, currency, `paymentType = credit_card`, category, the auto-resolved labels (or "(no label)" for cardholders not in `LabelMapping`), description note, counter-party.
8. Click **Import to Wallet**.
9. The page calls `/api/wallet/import/submit` once; the backend chunks the rows into batches of 50 and reports per-row outcomes. The view updates each row with success (✅ + new record id) or failure (❌ + error message).
10. Click **Reload comparison from Wallet** to re-fetch Wallet for the same window and confirm the successful rows are now on the Wallet side.

### What "good" looks like

- Comparison view loads in under ~5 s for a 30-day window with ≤200 existing Wallet records.
- Submit reports outcomes within ~5 s for up to 50 selected rows.
- Closing the tab mid-import does NOT undo accepted rows — they're committed in Wallet.

---

## 5. Add a cardholder to the label mapping

Whenever the operator wants a new cardholder section auto-labeled:

1. Find the cardholder's `RawName` as it appears in the PDF. The simplest way: upload the statement to the Compare endpoint; the response's `unmappedSections` array names them all.
2. Get the Wallet label id (`GET /v1/api/labels` or pull from the Wallet UI).
3. Add the entry to `Wallet:LabelMapping` in `appsettings.json` (or `appsettings.Development.json`).
4. Restart the backend. The new mapping takes effect on the next Compare.

There is no hot-reload — restarts are the deploy unit for config changes (FR-out-of-scope).

---

## 6. Run the test suite

### Backend

```bash
# from repo root
dotnet test
```

Notable new test classes:

- `tests/CardStatement.Tests/Wallet/DuplicateMatcherTests.cs` — table-driven for the FR-009 rule. Asserts determinism on repeated calls.
- `tests/CardStatement.Tests/Wallet/LabelMappingResolverTests.cs` — case-insensitive lookup, missing-key returns empty, distinct unmapped list.
- `tests/CardStatement.Tests/Wallet/WalletApiClientPagingTests.cs` — fake `HttpMessageHandler` walks 3 pages and accumulates.
- `tests/CardStatement.Api.Tests/Wallet/WalletEndpointsTests.cs` — full pipeline against the fake handler.
- `tests/CardStatement.Api.Tests/Wallet/WalletNotConfiguredTests.cs` — boots the backend with `Wallet:Jwt` blank and asserts the existing extract endpoint still works while every `/api/wallet/*` returns `WALLET_NOT_CONFIGURED`.
- `tests/CardStatement.Api.Tests/Wallet/WalletImportEndToEndTests.cs` — uses the bundled BAC sample PDF + a fake Wallet snapshot and asserts the comparison pairing.

None of these tests touch the live Wallet API; you can run them without a JWT.

### Frontend

```bash
cd frontend
pnpm test
```

New tests:

- `frontend/tests/walletImportPage.test.tsx` — RTL: dropdown populates → upload → comparison renders → submit happy path. `fetch` is stubbed.

### Regression check (existing flow not broken)

```bash
# Existing CardStatement.Tests + Api.Tests + frontend tests must keep passing untouched
dotnet test
cd frontend && pnpm test
```

If any pre-existing test fails after merging the Wallet feature, that's a regression — check whether the change to `App.tsx` / `main.tsx` accidentally altered the extract page render path, or whether `Program.cs` reordered DI registrations.

---

## 7. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Wallet Import page shows "Wallet credentials not configured" | `Wallet:Jwt` empty or missing | Configure `appsettings.Development.json` or env var; restart backend. |
| Banner says "Wallet credentials are not valid" | JWT expired / wrong tenant | Refresh the token from the Wallet provider; update config; restart. |
| Banner says "Wallet temporarily unavailable" | Network timeout / Wallet 5xx / DNS | Try again. If persistent, check `BaseUrl` + network. |
| Banner says "Wallet rejected the query" | Wallet 400 (bad accountId, bad date format, etc.) | Check the backend log for the upstream body excerpt; usually a stale account id chosen client-side. Pick a fresh account. |
| Compare view shows lots of "needs category" warnings | You haven't picked categories for every selected row | Either pick a category for each, or uncheck the rows you don't want to import. |
| Import outcome shows row failures with "category not found" | Category was deleted in Wallet after page load | Refresh the page (which re-fetches categories), retry. |
| Cardholder rows have "(no label)" in the preview but you expected a label | `LabelMapping` is missing the cardholder's `RawName` (or the name has trailing spaces / different case) | Confirm the exact `RawName` via the `unmappedSections` field of the Compare response; update `LabelMapping`; restart. |
| Backend log shows "WalletApiException(BodyExcerpt=…)" lines | Diagnostic — Wallet returned a non-success that the backend translated | Read the body excerpt; usually self-explanatory. |
| Records appear duplicated in Wallet after import | Likely the duplicate-detection window missed them (manual entry was >2 days off) | Verify against the `pairings` payload. Future spec may make the window configurable. |

---

## 8. What's *not* in this feature

(Restated from the spec so you don't go looking.)

- Editing or deleting Wallet records.
- Picking `paymentType` per row.
- Splitting one PDF across multiple Wallet accounts.
- Persisting comparison state across sessions / refreshes.
- OAuth / refresh tokens / multi-user auth.
- Hot-reload of `Wallet:Jwt` or `LabelMapping`.
- A bulk "apply this category to all selected" action.
- Adding a second bank (BAC only; multi-bank seam from `002` is in place, but no other bank is registered).
