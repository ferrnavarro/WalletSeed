# Quickstart: PDF Extract & Display (Web MVP)

**Feature**: `001-pdf-extract-web`
**Audience**: a developer setting up the new API + frontend locally for the first time.
**Prereqs**: .NET 10 SDK (`10.0.201`+), Node.js 20+, pnpm 9+, git.

## TL;DR

```bash
# Terminal 1 — backend
dotnet run --project src/CardStatement.Api

# Terminal 2 — frontend
cd frontend && pnpm install && pnpm dev
```

Open <http://localhost:5173>, upload `samples/final5140_45178439_316493_0.pdf`, and you should see the statement header, three+ cardholder sections, every transaction, and per-section + statement-level totals — within ~5 seconds.

---

## 1. Backend: `CardStatement.Api`

The API is a thin ASP.NET Core Minimal API that wraps the existing `CardStatement.Core` parser. No LLM, no Category/Labels API, no DB.

```bash
# From the repo root:
dotnet build CreditStatementParser.slnx          # builds Core, App, Api, both test projects
dotnet run --project src/CardStatement.Api       # listens on http://localhost:5080
```

Smoke-test the endpoint without the frontend:

```bash
curl -i -F "file=@samples/final5140_45178439_316493_0.pdf" \
  http://localhost:5080/api/statements/extract \
  | head -50
```

Expected: HTTP 200, `Content-Type: application/json`, body matching `specs/001-pdf-extract-web/contracts/openapi.yaml#/components/schemas/ExtractedStatementResponse`.

Error-path smoke tests:

```bash
# Wrong file type → 400 INVALID_FILE_TYPE
echo "not a pdf" > /tmp/fake.pdf
curl -i -F "file=@/tmp/fake.pdf" http://localhost:5080/api/statements/extract

# Empty file → 400 EMPTY_FILE
: > /tmp/empty.pdf
curl -i -F "file=@/tmp/empty.pdf;type=application/pdf" http://localhost:5080/api/statements/extract

# Oversized → 413 FILE_TOO_LARGE (generate a 28 MB pseudo-pdf)
head -c $((28 * 1024 * 1024)) /dev/urandom > /tmp/big.pdf
curl -i -F "file=@/tmp/big.pdf;type=application/pdf" http://localhost:5080/api/statements/extract
```

### Backend config (`src/CardStatement.Api/appsettings.json`)

```json
{
  "Kestrel": { "Limits": { "MaxRequestBodySize": 26214400 } },
  "Cors": { "AllowedOrigins": ["http://localhost:5173"] },
  "Upload": { "MaxBytes": 26214400 }
}
```

There are **no secrets** in this project — no Category/Labels/LLM keys needed (those are the next spec's problem). `appsettings.Development.json` is git-ignored and not required.

### Run backend tests

```bash
dotnet test tests/CardStatement.Api.Tests
```

The key assertion is `ExtractEndpointTests.HappyPath_MatchesGroundTruth` which locks in **SC-002** (row-for-row parity with `result.json` for the sample PDF).

---

## 2. Frontend: `frontend/`

```bash
cd frontend
pnpm install
pnpm dev          # Vite dev server on http://localhost:5173
pnpm test         # Vitest, watch mode
pnpm test --run   # Vitest, one-shot (CI mode)
pnpm build        # production build to frontend/dist
pnpm preview      # serve the production build for smoke tests
```

### One-time scaffold (only if `frontend/` does not yet exist)

```bash
pnpm create vite@latest frontend -- --template react-ts
cd frontend
pnpm install
pnpm add -D vitest @testing-library/react @testing-library/jest-dom @testing-library/user-event jsdom
```

Then wire Vitest in `vite.config.ts`:

```ts
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: { port: 5173 },
  test: { environment: 'jsdom', globals: true, setupFiles: ['./tests/setup.ts'] },
});
```

### Pointing the frontend at the backend

`frontend/src/api/statementsClient.ts` reads the API base URL from `import.meta.env.VITE_API_BASE_URL`. Default for dev: `http://localhost:5080`. Override per-environment by creating `frontend/.env.local`:

```bash
echo "VITE_API_BASE_URL=http://localhost:5080" > frontend/.env.local
```

---

## 3. End-to-end happy-path validation (covers SC-001, SC-002, SC-003, SC-005)

1. Start the backend: `dotnet run --project src/CardStatement.Api`
2. Start the frontend: `cd frontend && pnpm dev`
3. Open <http://localhost:5173>
4. Click "Choose file", select `samples/final5140_45178439_316493_0.pdf`, click "Extract".
5. **Within ~5 seconds** (SC-001 cap: 10 s) you should see:
   - Statement header at top: `VISA INFINITE BLACK`, `4593-78XX-XXXX-2145`, period `2026-05-21 → 2026-05-18`, page count `5`.
   - Multiple cardholder sections (`...2533`, `...2640`, `...2706`, `...4941`, `...5468` depending on the sample).
   - Every transaction row from the PDF — first row is `2026-04-18 / BURGER KING AHUACHAPAN / $2.00 / expense / cardLast4=2533` (matches `result.json:21-30`).
   - Per-section totals (computed + printed side by side; no mismatch).
   - Statement totals: `totalIncome = 877.01`, `totalExpense = 1462.19` (matches `result.json:12-14`).
   - Reconciliation status badge: `OK`.

If those values are off **at all**, the parity test in `ExtractEndpointTests.HappyPath_MatchesGroundTruth` should already have caught it — that test is the source of truth for SC-002.

---

## 4. End-to-end error-path validation (covers SC-006)

For each of the four error files below, the UI should show a distinct error message and remain immediately usable for another upload:

| File | Expected `code` | Expected message theme |
|---|---|---|
| `/tmp/not_a_pdf.txt` (any text file) | `INVALID_FILE_TYPE` | "Please upload a PDF file." |
| A scanned-only PDF (e.g. an image-only export) | `NO_TEXT_EXTRACTABLE` | "This PDF doesn't contain machine-readable text. Scanned PDFs aren't supported in this version." |
| A non-BAC bank statement PDF | `UNRECOGNIZED_LAYOUT` | "We couldn't recognize this as a BAC Credomatic statement." |
| A password-protected PDF | `PASSWORD_PROTECTED` | "This PDF is password-protected. Please remove the password and try again." |

The frontend rejects non-PDFs *before* hitting the backend (FR-002); the backend rejects them again as a defense-in-depth check (R3 in `research.md`).

---

## 5. Project layout reference

```text
src/
├── CardStatement.Core/          # existing parser library, untouched
├── CardStatement.App/           # existing console PoC, untouched
└── CardStatement.Api/           # NEW — ASP.NET Core Minimal API
tests/
├── CardStatement.Tests/         # existing Core tests, untouched
└── CardStatement.Api.Tests/     # NEW — endpoint contract tests
frontend/                        # NEW — Vite + React + TS + pnpm
samples/
└── final5140_45178439_316493_0.pdf
```

See `specs/001-pdf-extract-web/plan.md` §Project Structure for the full tree, and `specs/001-pdf-extract-web/data-model.md` for the wire shape produced by `StatementMapper`.

---

## 6. Troubleshooting

- **CORS error in the browser console** → the API isn't listening on 5080, or `Cors:AllowedOrigins` in `appsettings.json` doesn't include `http://localhost:5173`. Restart the API after editing config.
- **`405 Method Not Allowed`** on `POST /api/statements/extract` → the endpoint registration was skipped; check `Program.cs`.
- **`UnauthorizedAccessException` writing temp file** → the request handler writes the upload to `Path.GetTempFileName()` (see R7 in `research.md`); ensure the API process has write access to `$TMPDIR`.
- **Parse succeeds but the UI shows empty sections** → the response shape changed but the frontend types/components didn't update. Re-check `frontend/src/types/api.ts` against `specs/001-pdf-extract-web/contracts/openapi.yaml`.
- **`pnpm: command not found`** → install pnpm globally (`npm install -g pnpm` or `corepack enable && corepack prepare pnpm@latest --activate`).
- **Backend warns about missing `appsettings.Development.json`** → harmless; that file is git-ignored and not used by this spec.
