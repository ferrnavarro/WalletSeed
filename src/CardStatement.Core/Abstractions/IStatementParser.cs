using CardStatement.Core.Models;

namespace CardStatement.Core.Abstractions;

public interface IStatementParser
{
    Statement Parse(PdfDocumentWords words);
}
