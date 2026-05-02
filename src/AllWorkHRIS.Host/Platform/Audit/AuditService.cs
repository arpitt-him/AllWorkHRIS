using System.Security.Claims;
using Dapper;
using Microsoft.Extensions.Logging;
using AllWorkHRIS.Core.Audit;
using AllWorkHRIS.Core.Data;

namespace AllWorkHRIS.Host.Platform.Audit;

public sealed class AuditService : IAuditService
{
    // Phase 1-7: single dev tenant; replaced by claim lookup in Phase 8 multi-tenant wiring
    private static readonly Guid _platformTenantId = new("00000000-0000-0000-0000-000000000001");

    private readonly IConnectionFactory      _connectionFactory;
    private readonly IHttpContextAccessor    _httpContextAccessor;
    private readonly ILogger<AuditService>   _logger;

    public AuditService(
        IConnectionFactory    connectionFactory,
        IHttpContextAccessor  httpContextAccessor,
        ILogger<AuditService> logger)
    {
        _connectionFactory   = connectionFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger              = logger;
    }

    public async Task LogAsync(AuditEventRecord auditEvent)
    {
        try
        {
            var ctx              = _httpContextAccessor.HttpContext;
            var subClaim         = ctx?.User.FindFirstValue("sub");
            var actorUserId      = Guid.TryParse(subClaim, out var g) ? g : Guid.Empty;
            var actorDisplayName = ctx?.User.FindFirstValue("name")
                                   ?? ctx?.User.FindFirstValue("preferred_username");
            var ipAddress        = ctx?.Connection.RemoteIpAddress?.ToString();
            var sessionId        = ctx?.TraceIdentifier;

            const string sql = """
                INSERT INTO platform_audit_event (
                    audit_event_id, tenant_id, event_timestamp, event_type, module_name,
                    entity_type, entity_id, parent_entity_type, parent_entity_id,
                    actor_user_id, actor_display_name, change_summary,
                    before_state_json, after_state_json, outcome, failure_reason,
                    ip_address, session_id
                ) VALUES (
                    @AuditEventId, @TenantId, @EventTimestamp, @EventType, @ModuleName,
                    @EntityType, @EntityId, @ParentEntityType, @ParentEntityId,
                    @ActorUserId, @ActorDisplayName, @ChangeSummary,
                    @BeforeStateJson, @AfterStateJson, @Outcome, @FailureReason,
                    @IpAddress, @SessionId
                )
                """;

            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync(sql, new
            {
                AuditEventId     = Guid.NewGuid(),
                TenantId         = _platformTenantId,
                EventTimestamp   = DateTimeOffset.UtcNow,
                auditEvent.EventType,
                auditEvent.ModuleName,
                auditEvent.EntityType,
                auditEvent.EntityId,
                auditEvent.ParentEntityType,
                auditEvent.ParentEntityId,
                ActorUserId      = actorUserId,
                ActorDisplayName = actorDisplayName,
                auditEvent.ChangeSummary,
                BeforeStateJson  = auditEvent.BeforeJson,
                AfterStateJson   = auditEvent.AfterJson,
                auditEvent.Outcome,
                auditEvent.FailureReason,
                IpAddress        = ipAddress,
                SessionId        = sessionId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to write audit event {EventType} for {EntityType} {EntityId}",
                auditEvent.EventType, auditEvent.EntityType, auditEvent.EntityId);
        }
    }
}
