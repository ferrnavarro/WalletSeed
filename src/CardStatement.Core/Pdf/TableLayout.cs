namespace CardStatement.Core.Pdf;

public sealed record TableLayout(
    int PageNumber,
    double HeaderY,
    ColumnBands Bands);

public sealed record ColumnBands(
    double TransactionDateX,
    double PostingDateX,
    double ReferenceX,
    double SequenceX,
    double DescriptionLeftX,
    double DescriptionRightX,
    double CargosAbonosSplitX,
    double PageRightX);
