using Cya2.Core.Entities;

namespace Cya2.Core.Interfaces;

public interface IAuditEventRepository
{
    Task AddAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
    Task<int> DeleteExpiredAsync(DateTime expiresBeforeUtc, CancellationToken cancellationToken = default);
}
