using CardStatement.App.Output;
using CardStatement.Core.Models;
using CardStatement.Core.Parsing;
using CardStatement.Core.Pdf;
using CardStatement.Core.Reconciliation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CardStatement.App;

public static class CliRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return args.Length == 0 ? 2 : 0;
        }

        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            PrintUsage();
            return 2;
        }

        if (options.DumpWords) return DebugDump.DumpWords(options.PdfPath, options.DumpPage);
        if (options.DumpRows) return DebugDump.DumpRows(options.PdfPath, options.DumpPage);
        if (options.DumpParsed) return DebugDump.DumpParsed(options.PdfPath);

        return await RunPipelineAsync(options).ConfigureAwait(false);
    }

    private static async Task<int> RunPipelineAsync(CliOptions options)
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((ctx, cfg) =>
            {
                cfg.AddUserSecrets(typeof(CliRunner).Assembly, optional: true);
                cfg.AddEnvironmentVariables(prefix: "WALLETSEED_");
            })
            .ConfigureServices((ctx, services) => CompositionRoot.ConfigureServices(services, ctx.Configuration))
            .ConfigureLogging((ctx, logging) =>
            {
                if (options.Verbose) logging.SetMinimumLevel(LogLevel.Debug);
            })
            .Build();

        var pipeline = host.Services.GetRequiredService<Pipeline>();
        var writer = host.Services.GetRequiredService<OutputWriter>();

        try
        {
            var result = await pipeline.RunAsync(options.PdfPath).ConfigureAwait(false);
            await writer.WriteAsync(result, options.JsonOut, options.CsvOut).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            var logger = host.Services.GetRequiredService<ILogger<Pipeline>>();
            logger.LogError(ex, "Pipeline failed.");
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: CardStatement.App <path-to-pdf> [--out result.json] [--csv result.csv] [--verbose]");
        Console.WriteLine("Debug : CardStatement.App <path-to-pdf> --dump-words [--page N]");
        Console.WriteLine("Debug : CardStatement.App <path-to-pdf> --dump-rows  [--page N]");
        Console.WriteLine("Debug : CardStatement.App <path-to-pdf> --dump-parsed");
    }
}

internal static class DebugDump
{
    public static int DumpWords(string pdfPath, int? pageFilter)
    {
        var extractor = new PdfPigExtractor();
        var doc = extractor.Extract(pdfPath);
        Console.WriteLine($"# Pages: {doc.PageCount}  Words: {doc.Words.Count}");
        var words = pageFilter is int p
            ? doc.Words.Where(w => w.PageNumber == p)
            : doc.Words;

        foreach (var w in words.OrderBy(w => w.PageNumber).ThenByDescending(w => w.Y).ThenBy(w => w.X))
        {
            Console.WriteLine($"p{w.PageNumber}  x={w.X,7:0.00}  y={w.Y,7:0.00}  w={w.Width,6:0.00}  h={w.Height,5:0.00}  \"{w.Text}\"");
        }
        return 0;
    }

    public static int DumpRows(string pdfPath, int? pageFilter)
    {
        var extractor = new PdfPigExtractor();
        var locator = new TransactionTableLocator();
        var builder = new RowBuilder();

        var doc = extractor.Extract(pdfPath);
        var byPage = doc.Words.GroupBy(w => w.PageNumber)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PdfWord>)g.ToList());

        var layouts = locator.Locate(doc.Words).ToList();
        Console.WriteLine($"# Pages with table: {string.Join(", ", layouts.Select(l => l.PageNumber))}");

        foreach (var layout in layouts)
        {
            if (pageFilter is int p && p != layout.PageNumber) continue;
            var rows = builder.Build(layout, byPage[layout.PageNumber]);
            Console.WriteLine($"--- p{layout.PageNumber}  header_y={layout.HeaderY:0.00}  rows={rows.Count} ---");
            foreach (var row in rows)
            {
                var text = string.Join(" ", row.Words.Select(w => w.Text));
                Console.WriteLine($"  y={row.Y,7:0.00}  {text}");
            }
        }
        return 0;
    }

    public static int DumpParsed(string pdfPath)
    {
        var extractor = new PdfPigExtractor();
        var parser = new StatementParser();
        var reconciler = new Reconciler();

        var doc = extractor.Extract(pdfPath);
        var stmt = reconciler.Reconcile(parser.Parse(doc));

        Console.WriteLine($"Card type    : {stmt.CardType}");
        Console.WriteLine($"Account      : {stmt.MaskedAccount}");
        Console.WriteLine($"Period       : {stmt.Period.IssueDate:yyyy-MM-dd} → {stmt.Period.CutoffDate:yyyy-MM-dd}");
        Console.WriteLine($"Pages        : {stmt.PageCount}");
        Console.WriteLine($"Reconcile    : {stmt.ReconciliationStatus}");
        Console.WriteLine($"PrintedTotal : charges={stmt.PrintedTotalCharges} credits={stmt.PrintedTotalCredits}");
        Console.WriteLine();

        decimal totalExpense = 0m, totalIncome = 0m;
        foreach (var s in stmt.Sections)
        {
            var expense = s.Transactions.Where(t => t.Direction == Direction.Expense).Sum(t => t.Amount);
            var income = s.Transactions.Where(t => t.Direction == Direction.Income).Sum(t => t.Amount);
            totalExpense += expense;
            totalIncome += income;
            Console.WriteLine($"== {s.CardLast4}  {s.RawName}  [{s.ReconciliationStatus}]  txns={s.Transactions.Count}  expense={expense}  income={income}  printedSubCharges={s.PrintedSubtotalCharges}  printedSubCredits={s.PrintedSubtotalCredits}");
            foreach (var t in s.Transactions)
            {
                Console.WriteLine($"   {t.TransactionDate:yyyy-MM-dd}  {t.PostingDate:MM-dd}  {t.ReferenceNumber,-10} {t.SequenceCode,-4} {t.RowType,-9} {t.Direction,-7} {t.Amount,8:0.00}  {t.RawDescription}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"PARSED totalExpense={totalExpense}  totalIncome={totalIncome}");
        return 0;
    }
}

public sealed record CliOptions(
    string PdfPath,
    string? JsonOut,
    string? CsvOut,
    bool Verbose,
    bool DumpWords,
    bool DumpRows,
    bool DumpParsed,
    int? DumpPage)
{
    public static CliOptions Parse(string[] args)
    {
        string? pdf = null;
        string? json = null;
        string? csv = null;
        var verbose = false;
        var dumpWords = false;
        var dumpRows = false;
        var dumpParsed = false;
        int? dumpPage = null;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--out":
                    json = RequireValue(args, ref i, "--out");
                    break;
                case "--csv":
                    csv = RequireValue(args, ref i, "--csv");
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--dump-words":
                    dumpWords = true;
                    break;
                case "--dump-rows":
                    dumpRows = true;
                    break;
                case "--dump-parsed":
                    dumpParsed = true;
                    break;
                case "--page":
                    dumpPage = int.Parse(RequireValue(args, ref i, "--page"), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                default:
                    if (a.StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException($"Unknown option: {a}");
                    if (pdf is not null)
                        throw new ArgumentException($"Unexpected positional argument: {a}");
                    pdf = a;
                    break;
            }
        }

        if (pdf is null)
            throw new ArgumentException("Missing required positional argument: <path-to-pdf>");

        return new CliOptions(pdf, json, csv, verbose, dumpWords, dumpRows, dumpParsed, dumpPage);
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"Missing value for {flag}");
        return args[++i];
    }
}
