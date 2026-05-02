namespace AllWorkHRIS.Core.Audit;

public sealed class NullAuditService : IAuditService
{
    public Task LogAsync(AuditEventRecord auditEvent) => Task.CompletedTask;
}
