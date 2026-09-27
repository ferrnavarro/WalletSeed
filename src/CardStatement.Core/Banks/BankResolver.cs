using System;
using System.Collections.Generic;
using System.Linq;
using CardStatement.Api.ErrorHandling;
using CardStatement.Core.Abstractions;
using CardStatement.Core.Banks.Exceptions;
using CardStatement.Core.Models;
using Microsoft.Extensions.Logging;

namespace CardStatement.Core.Banks;

public sealed class BankResolver : IBankResolver
{
    private readonly IBankRegistry _registry;
    private readonly ILogger<BankResolver> _logger;

    public BankResolver(IBankRegistry registry, ILogger<BankResolver> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public (BankInfo Bank, Statement Statement) Resolve(PdfDocumentWords words)
    {
        var candidates = new List<(IBankProvider Provider, BankDetection Detection)>();

        foreach (var provider in _registry.Providers)
        {
            try
            {
                var detection = provider.Detect(words);
                if (detection.Matched)
                {
                    candidates.Add((provider, detection));
                }
            }
            catch (Exception ex)
            {
                // FR-008: one buggy bank cannot take down the endpoint
                _logger.LogError(ex, "Bank detector '{BankId}' threw an exception: {ExceptionType} - {ExceptionMessage}",
                    provider.Info.Id, ex.GetType().Name, ex.Message);
            }
        }

        if (candidates.Count == 0)
        {
            _logger.LogInformation("No bank matched; returning UNRECOGNIZED_LAYOUT");
            throw new NoBankMatchedException();
        }

        IBankProvider winner;
        if (candidates.Count > 1)
        {
            // Ambiguous detection
            var claimantsList = string.Join(", ", candidates.Select(c => $"{c.Provider.Info.Id}(conf={c.Detection.Confidence}, reason='{c.Detection.Reason}')"));
            _logger.LogWarning("Ambiguous detection: {Claimants}", claimantsList);

            // Sort by (-Confidence, Provider.Info.Id ordinal)
            var sorted = candidates
                .OrderByDescending(c => c.Detection.Confidence)
                .ThenBy(c => c.Provider.Info.Id, StringComparer.Ordinal)
                .ToList();

            winner = sorted[0].Provider;
        }
        else
        {
            winner = candidates[0].Provider;
        }

        _logger.LogInformation("Bank selected: {BankId}", winner.Info.Id);

        try
        {
            var statement = winner.Parse(words);
            return (winner.Info, statement);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bank '{BankId}' could not parse the PDF: {ExceptionType} - {ExceptionMessage}",
                winner.Info.Id, ex.GetType().Name, ex.Message);
            throw new UnrecognizedLayoutException($"Bank '{winner.Info.Id}' could not parse the PDF.", ex);
        }
    }
}
