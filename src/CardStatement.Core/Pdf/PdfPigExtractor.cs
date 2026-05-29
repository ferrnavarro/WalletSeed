using CardStatement.Core.Abstractions;
using CardStatement.Core.Models;
using UglyToad.PdfPig;

namespace CardStatement.Core.Pdf;

public sealed class PdfPigExtractor : IPdfExtractor
{
    private static readonly object LockObject = new();

    public PdfDocumentWords Extract(string pdfPath)
    {
        lock (LockObject)
        {
            using var doc = PdfDocument.Open(pdfPath);
            var words = new List<PdfWord>(capacity: 4096);
            var pageCount = doc.NumberOfPages;

        for (var pageNo = 1; pageNo <= pageCount; pageNo++)
        {
            var page = doc.GetPage(pageNo);
            foreach (var w in page.GetWords())
            {
                var bbox = w.BoundingBox;
                words.Add(new PdfWord(
                    PageNumber: pageNo,
                    Text: w.Text,
                    X: bbox.Left,
                    Y: bbox.Bottom,
                    Width: bbox.Width,
                    Height: bbox.Height));
            }
        }

        return new PdfDocumentWords(pageCount, words);
        }
    }
}
