using CardStatement.Core.Models;

namespace CardStatement.Core.Abstractions;

public interface IPdfExtractor
{
    PdfDocumentWords Extract(string pdfPath);
}

public sealed record PdfDocumentWords(int PageCount, IReadOnlyList<PdfWord> Words);
