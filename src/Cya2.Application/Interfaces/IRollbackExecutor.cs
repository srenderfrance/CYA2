using System.Threading;
using System.Threading.Tasks;

namespace Cya2.Application.Interfaces;

/// <summary>
/// Executes a rollback against the persistence layer. Database-specific transaction
/// and schema details belong to the implementing adapter.
/// </summary>
public interface IRollbackExecutor
{
    Task<RollbackResult> RollbackDonationsAsync(CancellationToken cancellationToken = default);
    Task<RollbackResult> RollbackAccountingAsync(CancellationToken cancellationToken = default);
}
