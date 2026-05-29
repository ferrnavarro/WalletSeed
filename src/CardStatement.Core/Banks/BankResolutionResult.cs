using CardStatement.Core.Abstractions;
using CardStatement.Core.Models;

namespace CardStatement.Core.Banks;

internal sealed record BankResolutionResult(IBankProvider Provider, Statement Statement);
