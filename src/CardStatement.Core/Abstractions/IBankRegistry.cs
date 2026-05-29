using System.Collections.Generic;

namespace CardStatement.Core.Abstractions;

public interface IBankRegistry
{
    IReadOnlyList<IBankProvider> Providers { get; }
}
