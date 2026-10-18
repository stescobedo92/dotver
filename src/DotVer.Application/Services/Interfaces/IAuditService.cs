using DotVer.Domain;

namespace DotVer.Application;

public interface IAuditService
{
    Task<IReadOnlyList<AuditEntry>> GetAuditAsync(CancellationToken cancellationToken);
}
