# Credit Card Statement Parser

A **.NET 10 console application (Proof of Concept)** that parses text-based **BAC Credomatic (El Salvador)** credit card statement PDFs, extracts transaction tables, reconstructs individual cardholder records, and enriches them via LLM categorization and configuration-based labeling.

## Key Features

1. **Deterministic Text Extraction**: Employs `UglyToad.PdfPig` to read characters alongside their spatial X/Y coordinates. Reconstructs transaction rows accurately while ignoring overlapping elements like header text, summary boxes, and bottom payment slips.
2. **BAC Grammar Parsing**: Detects purchase (`C####`), financing/reversal (`X####`), and payment (`P####`) rows, derives posting/transaction dates, handles Dec-Jan rollovers, and sets income/expense direction by coordinate mapping.
3. **LLM Expense Categorization**: Passes purchase descriptions to an LLM (such as OpenAI or compatible local models like Gemma and DeepSeek on LM Studio) to classify them against a Category API taxonomy.
4. **Cardholder Label Mapping**: Resolves cardholders to descriptive tags using a card-last-4-to-label-id map configured locally and validated against a Labels API.
5. **Reconciliation Engine**: Reconciles calculated transaction sums against printed section subtotals and grand totals to identify and flag discrepancies (`NeedsReview`).

---

## Solution Structure

```
CreditStatementParser.sln
├── src/
│   ├── CardStatement.Core/           Class Library containing models, PDF coordinate builders, APIs, and business logic
│   └── CardStatement.App/            Composition root, CLI runner, and CSV/JSON output writers
└── tests/
    └── CardStatement.Tests/          xUnit testing suite covering parsing, date rollovers, LLM clients, and End-to-End pipelines
```

---

## Setup & Configuration

Configure settings in [src/CardStatement.App/appsettings.json](src/CardStatement.App/appsettings.json). For development secrets, add them to `appsettings.Development.json` (git-ignored) or via C# User Secrets:

```json
{
  "Api": {
    "BaseUrl": "https://rest.budgetbakers.com/wallet/v1/api",
    "BearerToken": "YOUR_BEARER_TOKEN"
  },
  "Categorization": {
    "Provider": "openai", // Use "stub" to run offline without LLM
    "BatchSize": 30, // 30 for cloud LLMs, 1 for local slow LLMs
    "FixedCategoryNames": {
      "Payment": "Debt",
      "FinancingCharge": "Loan, interests",
      "FinancingReversal": "Refunds (tax, purchase)"
    },
    "OpenAi": {
      "Model": "gpt-4.1-mini",
      "ApiKey": "YOUR_OPENAI_API_KEY",
      "BaseUrl": null, // Custom API base endpoint (e.g. for LM Studio)
      "UseJsonMode": true // Disable (false) if local LLM runner throws 400 Bad Request
    }
  }
}
```

### Local LLMs (LM Studio Setup)
To use a local runner like **LM Studio** (e.g., with Gemma or DeepSeek-R1):
1. Enable the Local Server in LM Studio.
2. In LM Studio, increase your **Context Length** to at least **`8192`** or **`16384`** (essential to prevent `Channel Error` crashes on long prompts).
3. Set the following in your local configuration:
   * `BaseUrl`: `"http://localhost:1234/v1/"`
   * `Model`: Set to the exact model path identifier loaded in LM Studio.
   * `UseJsonMode`: `false` (bypasses strict schema forcing).
   * `BatchSize`: `1` (processes 1 transaction at a time to prevent context limits, or higher if prompt caching is active).
   * `ApiKey`: Can be left blank (defaults to `"lm-studio"`).

---

## How to Run

Execute the console app directly using the dotnet CLI from the project folder:

```bash
# Parse a PDF statement and save results to a JSON file
dotnet run --project src/CardStatement.App -- <path-to-pdf> --out result.json

# Parse a PDF and output both JSON and CSV
dotnet run --project src/CardStatement.App -- <path-to-pdf> --out result.json --csv result.csv

# Print verbose logs and debug prompts
dotnet run --project src/CardStatement.App -- <path-to-pdf> --out result.json --verbose
```

### Debugging Commands
You can dump raw intermediate pipeline structures for troubleshooting:
```bash
# Dump raw words and coordinates per page
dotnet run --project src/CardStatement.App -- <path-to-pdf> --dump-words --page 1

# Dump spatial rows clustered by Y coordinate
dotnet run --project src/CardStatement.App -- <path-to-pdf> --dump-rows --page 1

# Dump fully parsed BAC grammar structures before LLM enrichment
dotnet run --project src/CardStatement.App -- <path-to-pdf> --dump-parsed
```

## Web Application (Vite + React + Minimal API)

A modern, responsive web application for extracting credit card statement PDFs (BAC Credomatic) and displaying transactions, totals, and reconciliation warnings in real-time.

Detailed documentation is available at [quickstart.md](specs/001-pdf-extract-web/quickstart.md).

### Running Locally

1. **Start the backend API**:
   ```bash
   dotnet run --project src/CardStatement.Api
   ```
   The API will listen at `http://localhost:5080`.

2. **Start the frontend**:
   ```bash
   cd frontend
   pnpm dev
   ```
   The Vite dev server will run at `http://localhost:5173`.

---

## Running Tests

Execute the xUnit suite to run all unit, integration, and End-to-End tests:
```bash
dotnet test
```
