using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CardStatement.Core.Abstractions;
using CardStatement.Core.Banks.Exceptions;

namespace CardStatement.Core.Banks;

public sealed class BankRegistry : IBankRegistry
{
    public IReadOnlyList<IBankProvider> Providers { get; }

    public BankRegistry(IEnumerable<IBankProvider> providers)
    {
        var snapshot = providers.ToImmutableArray();
        if (snapshot.IsEmpty)
            throw new EmptyBankRegistryException();

        // Detect duplicate ids early — they would silently change the tie-break
        var duplicates = snapshot
            .GroupBy(p => p.Info.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicates.Length > 0)
            throw new DuplicateBankIdException(duplicates);

        Providers = snapshot;
    }
}
