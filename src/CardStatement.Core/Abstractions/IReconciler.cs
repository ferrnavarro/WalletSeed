using CardStatement.Core.Models;

namespace CardStatement.Core.Abstractions;

public interface IReconciler
{
    Statement Reconcile(Statement statement);
}
