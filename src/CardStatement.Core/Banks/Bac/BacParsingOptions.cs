namespace CardStatement.Core.Banks.Bac;

public sealed class BacParsingOptions
{
    public double RowYTolerance { get; set; } = 2.0;
    public double ColumnXTolerance { get; set; } = 5.0;

    public double PageFooterCutoffY { get; set; } = 95.0;

    public double CargosAbonosSplitX { get; set; } = 520.0;
}
