using CardStatement.Core.Banks;
using CardStatement.Core.Models;

namespace CardStatement.Core.Abstractions;

public interface IBankResolver
{
    (BankInfo Bank, Statement Statement) Resolve(PdfDocumentWords words);
}
