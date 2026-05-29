using CardStatement.Core.Banks;
using CardStatement.Core.Models;

namespace CardStatement.Core.Abstractions;

public interface IBankProvider
{
    BankInfo Info { get; }
    BankDetection Detect(PdfDocumentWords words);
    Statement Parse(PdfDocumentWords words);
}
