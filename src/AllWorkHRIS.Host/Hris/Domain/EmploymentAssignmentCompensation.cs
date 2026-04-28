using AllWorkHRIS.Host.Hris.Commands;

namespace AllWorkHRIS.Host.Hris.Domain;

public sealed record Employment
{
    public Guid EmploymentId { get; init; }
    public Guid PersonId { get; init; }
    public Guid LegalEntityId { get; init; }
    public Guid EmployerId { get; init; }
    public string EmployeeNumber { get; init; } = default!;
    public EmploymentType EmploymentType { get; init; }
    public DateOnly EmploymentStartDate { get; init; }
    public DateOnly? EmploymentEndDate { get; init; }
    public DateOnly? OriginalHireDate { get; init; }
    public DateOnly? TerminationDate { get; init; }
    public EmploymentStatus EmploymentStatus { get; init; }
    public FullPartTimeStatus FullOrPartTimeStatus { get; init; }
    public RegularTemporaryStatus RegularOrTemporaryStatus { get; init; }
    public FlsaStatus FlsaStatus { get; init; }
    public Guid? PayrollContextId { get; init; }
    public Guid PrimaryWorkLocationId { get; init; }
    public Guid PrimaryDepartmentId { get; init; }
    public Guid? ManagerEmploymentId { get; init; }
    public bool RehireFlag { get; init; }
    public Guid? PriorEmploymentId { get; init; }
    public bool PrimaryFlag { get; init; }
    public bool PayrollEligibilityFlag { get; init; }
    public bool BenefitsEligibilityFlag { get; init; }
    public bool TimeTrackingRequiredFlag { get; init; }
    public DateTimeOffset CreationTimestamp { get; init; }
    public DateTimeOffset LastUpdateTimestamp { get; init; }
    public string LastUpdatedBy { get; init; } = default!;

    public static Employment CreateFromHire(HireEmployeeCommand command, Guid personId)
    {
        var now = DateTimeOffset.UtcNow;
        return new Employment
        {
            EmploymentId              = Guid.NewGuid(),
            PersonId                  = personId,
            LegalEntityId             = command.LegalEntityId,
            EmployerId                = command.LegalEntityId,
            EmployeeNumber            = command.EmployeeNumber,
            EmploymentType            = Enum.Parse<EmploymentType>(command.EmploymentType, ignoreCase: true),
            EmploymentStartDate       = command.EmploymentStartDate,
            OriginalHireDate          = command.EmploymentStartDate,
            EmploymentStatus          = EmploymentStatus.Active,
            FullOrPartTimeStatus      = Enum.Parse<FullPartTimeStatus>(command.FullOrPartTimeStatus, ignoreCase: true),
            RegularOrTemporaryStatus  = RegularTemporaryStatus.Regular,
            FlsaStatus                = Enum.Parse<FlsaStatus>(command.FlsaStatus, ignoreCase: true),
            PayrollContextId          = command.PayrollContextId,
            PrimaryWorkLocationId     = command.LocationId,
            PrimaryDepartmentId       = command.DepartmentId,
            ManagerEmploymentId       = command.ManagerEmploymentId,
            RehireFlag                = false,
            PrimaryFlag               = true,
            PayrollEligibilityFlag    = true,
            BenefitsEligibilityFlag   = true,
            TimeTrackingRequiredFlag  = false,
            CreationTimestamp         = now,
            LastUpdateTimestamp       = now,
            LastUpdatedBy             = command.InitiatedBy.ToString()
        };
    }
}

public sealed record Assignment
{
    public Guid AssignmentId { get; init; }
    public Guid EmploymentId { get; init; }
    public Guid JobId { get; init; }
    public Guid? PositionId { get; init; }
    public Guid DepartmentId { get; init; }
    public Guid LocationId { get; init; }
    public Guid PayrollContextId { get; init; }
    public Guid? PlanId { get; init; }
    public AssignmentType AssignmentType { get; init; }
    public AssignmentStatus AssignmentStatus { get; init; }
    public int? AssignmentPriority { get; init; }
    public DateOnly AssignmentStartDate { get; init; }
    public DateOnly? AssignmentEndDate { get; init; }
    public Guid CreatedBy { get; init; }
    public DateTimeOffset CreationTimestamp { get; init; }
    public Guid LastUpdatedBy { get; init; }
    public DateTimeOffset LastUpdateTimestamp { get; init; }

    public static Assignment CreateInitial(HireEmployeeCommand command, Guid employmentId)
    {
        var now = DateTimeOffset.UtcNow;
        return new Assignment
        {
            AssignmentId      = Guid.NewGuid(),
            EmploymentId      = employmentId,
            JobId             = command.JobId,
            PositionId        = command.PositionId,
            DepartmentId      = command.DepartmentId,
            LocationId        = command.LocationId,
            PayrollContextId  = command.PayrollContextId ?? Guid.Empty,
            AssignmentType    = AssignmentType.Primary,
            AssignmentStatus  = AssignmentStatus.Active,
            AssignmentStartDate = command.EmploymentStartDate,
            CreatedBy         = command.InitiatedBy,
            CreationTimestamp = now,
            LastUpdatedBy     = command.InitiatedBy,
            LastUpdateTimestamp = now
        };
    }
}

public sealed record CompensationRecord
{
    public Guid CompensationId { get; init; }
    public Guid EmploymentId { get; init; }
    public CompensationRateType RateType { get; init; }
    public decimal BaseRate { get; init; }
    public string RateCurrency { get; init; } = "USD";
    public decimal? AnnualEquivalent { get; init; }
    public PayFrequency PayFrequency { get; init; }
    public DateOnly EffectiveStartDate { get; init; }
    public DateOnly? EffectiveEndDate { get; init; }
    public CompensationStatus CompensationStatus { get; init; }
    public string ChangeReasonCode { get; init; } = default!;
    public ApprovalStatus ApprovalStatus { get; init; }
    public Guid? ApprovedBy { get; init; }
    public DateTimeOffset? ApprovalTimestamp { get; init; }
    public bool PrimaryRateFlag { get; init; }
    public Guid CreatedBy { get; init; }
    public DateTimeOffset CreationTimestamp { get; init; }
    public Guid LastUpdatedBy { get; init; }
    public DateTimeOffset LastUpdateTimestamp { get; init; }

    public static CompensationRecord CreateInitial(HireEmployeeCommand command, Guid employmentId)
    {
        var now = DateTimeOffset.UtcNow;
        return new CompensationRecord
        {
            CompensationId      = Guid.NewGuid(),
            EmploymentId        = employmentId,
            RateType            = Enum.Parse<CompensationRateType>(command.RateType, ignoreCase: true),
            BaseRate            = command.BaseRate,
            RateCurrency        = "USD",
            PayFrequency        = Enum.Parse<PayFrequency>(command.PayFrequency, ignoreCase: true),
            EffectiveStartDate  = command.EmploymentStartDate,
            CompensationStatus  = CompensationStatus.Active,
            ChangeReasonCode    = command.ChangeReasonCode,
            ApprovalStatus      = ApprovalStatus.Approved,
            PrimaryRateFlag     = true,
            CreatedBy           = command.InitiatedBy,
            CreationTimestamp   = now,
            LastUpdatedBy       = command.InitiatedBy,
            LastUpdateTimestamp = now
        };
    }
}
