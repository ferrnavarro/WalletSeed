using CardStatement.Core.Models;

namespace CardStatement.Core.Categorization;

public sealed class FixedCategoryResolver
{
    private readonly IReadOnlyDictionary<Guid, Category> _byId;
    private readonly Category? _payment;
    private readonly Category? _financingCharge;
    private readonly Category? _financingReversal;
    private readonly Category? _fallback;

    public FixedCategoryResolver(
        IEnumerable<Category> taxonomy,
        FixedCategoryNamesOptions names,
        Guid? fallbackCategoryId)
    {
        var list = taxonomy.ToList();
        _byId = list.ToDictionary(c => c.Id);
        _payment = list.FirstOrDefault(c => string.Equals(c.Name, names.Payment, StringComparison.OrdinalIgnoreCase));
        _financingCharge = list.FirstOrDefault(c => string.Equals(c.Name, names.FinancingCharge, StringComparison.OrdinalIgnoreCase));
        _financingReversal = list.FirstOrDefault(c => string.Equals(c.Name, names.FinancingReversal, StringComparison.OrdinalIgnoreCase));

        if (fallbackCategoryId is Guid id && _byId.TryGetValue(id, out var fallback))
            _fallback = fallback;
    }

    public Category? Payment => _payment;
    public Category? FinancingCharge => _financingCharge;
    public Category? FinancingReversal => _financingReversal;
    public Category? Fallback => _fallback;

    public Category? ResolveForFixedRow(Transaction transaction)
    {
        return transaction.RowType switch
        {
            RowType.Payment => _payment ?? _fallback,
            RowType.Financing or RowType.Adjustment when transaction.Direction == Direction.Income => _financingReversal ?? _fallback,
            RowType.Financing or RowType.Adjustment => _financingCharge ?? _fallback,
            _ => null,
        };
    }

    public bool IsAllowed(Guid id) => _byId.ContainsKey(id);

    public Category? LookupById(Guid id) => _byId.TryGetValue(id, out var c) ? c : null;

    public IReadOnlyList<string> ValidateConfiguration(FixedCategoryNamesOptions names)
    {
        var warnings = new List<string>();
        if (_payment is null) warnings.Add($"Categorization.FixedCategoryNames.Payment '{names.Payment}' not found in taxonomy.");
        if (_financingCharge is null) warnings.Add($"Categorization.FixedCategoryNames.FinancingCharge '{names.FinancingCharge}' not found in taxonomy.");
        if (_financingReversal is null) warnings.Add($"Categorization.FixedCategoryNames.FinancingReversal '{names.FinancingReversal}' not found in taxonomy.");
        if (_fallback is null) warnings.Add("Categorization.FallbackCategoryId not present in taxonomy.");
        return warnings;
    }
}
