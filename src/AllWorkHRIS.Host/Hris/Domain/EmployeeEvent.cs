using AllWorkHRIS.Host.Hris.Commands;

namespace AllWorkHRIS.Host.Hris.Domain;

public enum EmployeeEventType
{
    Hire,
    Rehire,
    Termination,
    CompensationChange,
    AssignmentChange,
    Transfer,
    LeaveStart,
    LeaveReturn,
    StatusChange,
    ManagerChange,
    Correction
}

public sealed record EmployeeEvent
{
    public Guid EventId { get; init; }
    public Guid EmploymentId { get; init; }
    public EmployeeEventType EventType { get; init; }
    public DateOnly EffectiveDate { get; init; }
    public string? EventReason { get; init; }
    public string? Notes { get; init; }
    public Guid InitiatedBy { get; init; }
    public Guid? ApprovedBy { get; init; }
    public DateTimeOffset? ApprovalTimestamp { get; init; }
    public DateTimeOffset CreationTimestamp { get; init; }

    public static EmployeeEvent CreateHire(Guid personId, Guid employmentId, HireEmployeeCommand command)
    {
        return new EmployeeEvent
        {
            EventId           = Guid.NewGuid(),
            EmploymentId      = employmentId,
            EventType         = EmployeeEventType.Hire,
            EffectiveDate     = command.EmploymentStartDate,
            EventReason       = command.ChangeReasonCode,
            InitiatedBy       = command.InitiatedBy,
            CreationTimestamp = DateTimeOffset.UtcNow
        };
    }

    public static EmployeeEvent CreateTermination(Guid employmentId, TerminateEmployeeCommand command)
    {
        return new EmployeeEvent
        {
            EventId           = Guid.NewGuid(),
            EmploymentId      = employmentId,
            EventType         = EmployeeEventType.Termination,
            EffectiveDate     = command.TerminationDate,
            EventReason       = command.ReasonCode,
            Notes             = command.Notes,
            InitiatedBy       = command.InitiatedBy,
            CreationTimestamp = DateTimeOffset.UtcNow
        };
    }

    public static EmployeeEvent CreateCompensationChange(Guid employmentId, ChangeCompensationCommand command)
    {
        return new EmployeeEvent
        {
            EventId           = Guid.NewGuid(),
            EmploymentId      = employmentId,
            EventType         = EmployeeEventType.CompensationChange,
            EffectiveDate     = command.EffectiveDate,
            EventReason       = command.ChangeReasonCode,
            InitiatedBy       = command.InitiatedBy,
            CreationTimestamp = DateTimeOffset.UtcNow
        };
    }
}
