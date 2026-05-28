namespace CardStatement.Core.Models;

public sealed record PdfWord(
    int PageNumber,
    string Text,
    double X,
    double Y,
    double Width,
    double Height)
{
    public double Right => X + Width;
    public double CenterX => X + Width / 2.0;
}
