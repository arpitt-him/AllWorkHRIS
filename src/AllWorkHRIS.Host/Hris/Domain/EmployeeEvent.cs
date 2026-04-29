using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Host.Hris.Commands;

namespace AllWorkHRIS.Host.Hris.Domain;

public sealed record EmployeeEvent
{
    public Guid EventId { get; init; }
    public Guid EmploymentId { get; init; }
    public int EventTypeId { get; init; }
    public DateOnly EffectiveDate { get; init; }
    public string? EventReason { get; init; }
    public string? Notes { get; init; }
    public Guid InitiatedBy { get; init; }
    public Guid? ApprovedBy { get; init; }
    public DateTimeOffset? ApprovalTimestamp { get; init; }
    public DateTimeOffset CreationTimestamp { get; init; }

    public static EmployeeEvent CreateHire(
        Guid employmentId, HireEmployeeCommand command, ILookupCache lookupCache)
    {
        return new EmployeeEvent
        {
            EventId           = Guid.NewGuid(),
            EmploymentId      = employmentId,
            EventTypeId       = lookupCache.GetId(LookupTables.EmployeeEventType, "HIRE"),
            EffectiveDate     = command.EmploymentStartDate,
            EventReason       = command.ChangeReasonCode,
            InitiatedBy       = command.InitiatedBy,
            CreationTimestamp = DateTimeOffset.UtcNow
        };
    }

    public static EmployeeEvent CreateRehire(
        Guid employmentId, RehireEmployeeCommand command, ILookupCache lookupCache)
    {
        return new EmployeeEvent
        {
            EventId           = Guid.NewGuid(),
            EmploymentId      = employmentId,
            EventTypeId       = lookupCache.GetId(LookupTables.EmployeeEventType, "REHIRE"),
            EffectiveDate     = command.EmploymentStartDate,
            EventReason       = command.ChangeReasonCode,
            InitiatedBy       = command.InitiatedBy,
            CreationTimestamp = DateTimeOffset.UtcNow
        };
    }

    public static EmployeeEvent CreateTermination(
        Guid employmentId, TerminateEmployeeCommand command, ILookupCache lookupCache)
    {
        return new EmployeeEvent
        {
            EventId           = Guid.NewGuid(),
            EmploymentId      = employmentId,
            EventTypeId       = lookupCache.GetId(LookupTables.EmployeeEventType, "TERMINATION"),
            EffectiveDate     = command.TerminationDate,
            EventReason       = command.ReasonCode,
            Notes             = command.Notes,
            InitiatedBy       = command.InitiatedBy,
            CreationTimestamp = DateTimeOffset.UtcNow
        };
    }

    public static EmployeeEvent CreateCompensationChange(
        Guid employmentId, ChangeCompensationCommand command, ILookupCache lookupCache)
    {
        return new EmployeeEvent
        {
            EventId           = Guid.NewGuid(),
            EmploymentId      = employmentId,
            EventTypeId       = lookupCache.GetId(LookupTables.EmployeeEventType, "COMPENSATION_CHANGE"),
            EffectiveDate     = command.EffectiveDate,
            EventReason       = command.ChangeReasonCode,
            InitiatedBy       = command.InitiatedBy,
            CreationTimestamp = DateTimeOffset.UtcNow
        };
    }
}
