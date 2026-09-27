using System;
using System.Collections.Generic;

namespace CardStatement.Core.Banks.Exceptions;

public sealed class DuplicateBankIdException : Exception
{
    public IReadOnlyList<string> DuplicateIds { get; }

    public DuplicateBankIdException(IReadOnlyList<string> duplicateIds)
        : base($"Multiple IBankProvider implementations registered with the same Id(s): {string.Join(", ", duplicateIds)}.")
    {
        DuplicateIds = duplicateIds;
    }
}
