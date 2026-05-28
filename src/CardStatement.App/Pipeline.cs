using CardStatement.Core.Abstractions;
using CardStatement.Core.Categorization;
using CardStatement.Core.Labels;
using CardStatement.Core.Models;
using CardStatement.Core.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardStatement.App;

public sealed class Pipeline
{
    private readonly IPdfExtractor _pdf;
    private readonly IStatementParser _parser;
    private readonly IReconciler _reconciler;
    private readonly ICategoryApi _categoryApi;
    private readonly ILabelsApi _labelsApi;
    private readonly ILlmClient _llm;
    private readonly IOptions<CategorizationOptions> _catOpts;
    private readonly IOptions<CardholderLabelOptions> _labelOpts;
    private readonly ILogger<Pipeline> _logger;

    public Pipeline(
        IPdfExtractor pdf,
        IStatementParser parser,
        IReconciler reconciler,
        ICategoryApi categoryApi,
        ILabelsApi labelsApi,
        ILlmClient llm,
        IOptions<CategorizationOptions> catOpts,
        IOptions<CardholderLabelOptions> labelOpts,
        ILogger<Pipeline> logger)
    {
        _pdf = pdf;
        _parser = parser;
        _reconciler = reconciler;
        _categoryApi = categoryApi;
        _labelsApi = labelsApi;
        _llm = llm;
        _catOpts = catOpts;
        _labelOpts = labelOpts;
        _logger = logger;
    }

    public async Task<StatementResult> RunAsync(string pdfPath, CancellationToken ct = default)
    {
        _logger.LogInformation("Extracting PDF: {Path}", pdfPath);
        var words = _pdf.Extract(pdfPath);

        _logger.LogInformation("Parsing statement ({Pages} pages, {Words} words).", words.PageCount, words.Words.Count);
        var statement = _reconciler.Reconcile(_parser.Parse(words));

        _logger.LogInformation("Fetching categories...");
        var taxonomy = await _categoryApi.GetAllAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Loaded {Count} categories.", taxonomy.Count);

        _logger.LogInformation("Fetching labels...");
        var labels = await _labelsApi.GetAllAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Loaded {Count} labels (archived filtered).", labels.Count);

        var labelResolver = new LabelResolver(_labelOpts.Value.Map, labels);
        foreach (var warning in labelResolver.ValidateConfiguration())
            _logger.LogWarning("{Warning}", warning);

        var fixedResolver = new FixedCategoryResolver(taxonomy, _catOpts.Value.FixedCategoryNames, _catOpts.Value.FallbackCategoryId);
        foreach (var warning in fixedResolver.ValidateConfiguration(_catOpts.Value.FixedCategoryNames))
            _logger.LogWarning("{Warning}", warning);

        var categorizer = new LlmCategorizer(_llm, fixedResolver, taxonomy, _catOpts.Value);

        var builder = new ResultBuilder(labelResolver, categorizer);
        var result = await builder.BuildAsync(statement, ct).ConfigureAwait(false);

        if (statement.ReconciliationStatus == ReconciliationStatus.Mismatch)
            _logger.LogWarning("Reconciliation mismatch — parsed totals do not match printed TOTAL.");

        return result;
    }
}
