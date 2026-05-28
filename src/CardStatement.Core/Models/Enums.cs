namespace CardStatement.Core.Models;

public enum Direction
{
    Income,
    Expense,
}

public enum RowType
{
    Purchase,
    Financing,
    Payment,
    Adjustment,
}

public enum ReconciliationStatus
{
    NotChecked,
    Ok,
    Mismatch,
}
