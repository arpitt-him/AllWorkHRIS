namespace AllWorkHRIS.Core.Audit;

public sealed record AuditEventRecord(
    string  EventType,
    string  EntityType,
    Guid?   EntityId,
    string  ModuleName,
    string  ChangeSummary,
    string? BeforeJson       = null,
    string? AfterJson        = null,
    string? ParentEntityType = null,
    Guid?   ParentEntityId   = null,
    string  Outcome          = "SUCCESS",
    string? FailureReason    = null
);

public interface IAuditService
{
    Task LogAsync(AuditEventRecord auditEvent);
}
