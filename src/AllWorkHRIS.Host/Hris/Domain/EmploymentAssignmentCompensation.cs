using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Host.Hris.Commands;

namespace AllWorkHRIS.Host.Hris.Domain;


public sealed record Employment
{
    public Guid EmploymentId { get; init; }
    public Guid PersonId { get; init; }
    public Guid LegalEntityId { get; init; }
    public Guid EmployerId { get; init; }
    public string EmployeeNumber { get; init; } = default!;
    public int EmploymentTypeId { get; init; }
    public DateOnly EmploymentStartDate { get; init; }
    public DateOnly? EmploymentEndDate { get; init; }
    public DateOnly? OriginalHireDate { get; init; }
    public DateOnly? TerminationDate { get; init; }
    public int EmploymentStatusId { get; init; }
    public int FullPartTimeStatusId { get; init; }
    public int RegularTemporaryStatusId { get; init; }
    public int FlsaStatusId { get; init; }
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

    public static Employment CreateFromHire(
        HireEmployeeCommand command, Guid personId, ILookupCache lookupCache)
    {
        var now = DateTimeOffset.UtcNow;
        return new Employment
        {
            EmploymentId             = Guid.NewGuid(),
            PersonId                 = personId,
            LegalEntityId            = command.LegalEntityId,
            EmployerId               = command.LegalEntityId,
            EmployeeNumber           = command.EmployeeNumber,
            EmploymentTypeId         = command.EmploymentTypeId,
            EmploymentStartDate      = command.EmploymentStartDate,
            OriginalHireDate         = command.EmploymentStartDate,
            EmploymentStatusId       = lookupCache.GetId(LookupTables.EmploymentStatus, "ACTIVE"),
            FullPartTimeStatusId     = command.FullPartTimeStatusId,
            RegularTemporaryStatusId = lookupCache.GetId(LookupTables.RegularTemporaryStatus, "REGULAR"),
            FlsaStatusId             = command.FlsaStatusId,
            PayrollContextId         = command.PayrollContextId,
            PrimaryWorkLocationId    = command.LocationId,
            PrimaryDepartmentId      = command.DepartmentId,
            ManagerEmploymentId      = command.ManagerEmploymentId,
            RehireFlag               = false,
            PrimaryFlag              = true,
            PayrollEligibilityFlag   = true,
            BenefitsEligibilityFlag  = true,
            TimeTrackingRequiredFlag = false,
            CreationTimestamp        = now,
            LastUpdateTimestamp      = now,
            LastUpdatedBy            = command.InitiatedBy.ToString()
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
    public Guid? PayrollContextId { get; init; }
    public Guid? PlanId { get; init; }
    public int AssignmentTypeId { get; init; }
    public int AssignmentStatusId { get; init; }
    public int? AssignmentPriority { get; init; }
    public DateOnly AssignmentStartDate { get; init; }
    public DateOnly? AssignmentEndDate { get; init; }
    public Guid CreatedBy { get; init; }
    public DateTimeOffset CreationTimestamp { get; init; }
    public Guid LastUpdatedBy { get; init; }
    public DateTimeOffset LastUpdateTimestamp { get; init; }

    public static Assignment CreateInitial(
        HireEmployeeCommand command, Guid employmentId, ILookupCache lookupCache)
    {
        var now = DateTimeOffset.UtcNow;
        return new Assignment
        {
            AssignmentId        = Guid.NewGuid(),
            EmploymentId        = employmentId,
            JobId               = command.JobId,
            PositionId          = command.PositionId,
            DepartmentId        = command.DepartmentId,
            LocationId          = command.LocationId,
            PayrollContextId    = command.PayrollContextId,
            AssignmentTypeId    = lookupCache.GetId(LookupTables.AssignmentType, "PRIMARY"),
            AssignmentStatusId  = lookupCache.GetId(LookupTables.AssignmentStatus, "ACTIVE"),
            AssignmentStartDate = command.EmploymentStartDate,
            CreatedBy           = command.InitiatedBy,
            CreationTimestamp   = now,
            LastUpdatedBy       = command.InitiatedBy,
            LastUpdateTimestamp = now
        };
    }
}

public sealed record CompensationRecord
{
    public Guid CompensationId { get; init; }
    public Guid EmploymentId { get; init; }
    public int RateTypeId { get; init; }
    public int? PayTypeId { get; init; }
    public decimal BaseRate { get; init; }
    public string RateCurrency { get; init; } = "USD";
    public decimal? AnnualEquivalent { get; init; }
    public int PayFrequencyId { get; init; }
    public DateOnly EffectiveStartDate { get; init; }
    public DateOnly? EffectiveEndDate { get; init; }
    public int CompensationStatusId { get; init; }
    public string ChangeReasonCode { get; init; } = default!;
    public int ApprovalStatusId { get; init; }
    public Guid? ApprovedBy { get; init; }
    public DateTimeOffset? ApprovalTimestamp { get; init; }
    public bool PrimaryRateFlag { get; init; }
    public Guid CreatedBy { get; init; }
    public DateTimeOffset CreationTimestamp { get; init; }
    public Guid LastUpdatedBy { get; init; }
    public DateTimeOffset LastUpdateTimestamp { get; init; }

    public static CompensationRecord CreateInitial(
        HireEmployeeCommand command, Guid employmentId, ILookupCache lookupCache)
    {
        var now          = DateTimeOffset.UtcNow;
        var rateTypeCode = lookupCache.GetCode(LookupTables.CompensationRateType, command.RateTypeId);
        var flsaCode     = lookupCache.GetCode(LookupTables.FlsaStatus,           command.FlsaStatusId);
        var freqCode     = lookupCache.GetCode(LookupTables.PayFrequency,          command.PayFrequencyId);

        int      effectiveRateTypeId;
        decimal  effectiveBaseRate;
        decimal? annualEquivalent;
        if (rateTypeCode == "SALARY" && flsaCode == "NON_EXEMPT")
        {
            // Salaried non-exempt: engine uses base_rate as hourly, so derive it from the entered annual salary.
            effectiveRateTypeId = lookupCache.GetId(LookupTables.CompensationRateType, "HOURLY");
            annualEquivalent    = command.BaseRate;
            effectiveBaseRate   = Math.Round(command.BaseRate / 2080m, 4, MidpointRounding.AwayFromZero);
        }
        else
        {
            effectiveRateTypeId = command.RateTypeId;
            effectiveBaseRate   = command.BaseRate;
            annualEquivalent    = ComputeAnnualEquivalent(rateTypeCode, command.BaseRate, freqCode);
        }

        return new CompensationRecord
        {
            CompensationId       = Guid.NewGuid(),
            EmploymentId         = employmentId,
            RateTypeId           = effectiveRateTypeId,
            PayTypeId            = ResolvePayTypeId(rateTypeCode, lookupCache),
            BaseRate             = effectiveBaseRate,
            RateCurrency         = "USD",
            AnnualEquivalent     = annualEquivalent,
            PayFrequencyId       = command.PayFrequencyId,
            EffectiveStartDate   = command.EmploymentStartDate,
            CompensationStatusId = lookupCache.GetId(LookupTables.CompensationStatus, "ACTIVE"),
            ChangeReasonCode     = command.ChangeReasonCode,
            ApprovalStatusId     = lookupCache.GetId(LookupTables.ApprovalStatus, "APPROVED"),
            PrimaryRateFlag      = true,
            CreatedBy            = command.InitiatedBy,
            CreationTimestamp    = now,
            LastUpdatedBy        = command.InitiatedBy,
            LastUpdateTimestamp  = now
        };
    }

    // Maps the user-selected rate type code to the display-layer pay type.
    // Uses the original code (before SALARY+NON_EXEMPT normalization) so the stored
    // pay_type always reflects what the user intended, not the internal calculation mechanism.
    public static int? ResolvePayTypeId(string rateTypeCode, ILookupCache lookupCache)
        => lookupCache.GetAll(LookupTables.PayType)
               .FirstOrDefault(e => string.Equals(e.Code, rateTypeCode, StringComparison.OrdinalIgnoreCase))
               ?.Id;

    public static decimal? ComputeAnnualEquivalent(string rateTypeCode, decimal baseRate, string payFrequencyCode)
        => rateTypeCode switch
        {
            "SALARY"  => baseRate,
            "HOURLY"  => baseRate * 2080m,
            "DAILY"   => baseRate * 260m,
            "STIPEND" => baseRate * PeriodsPerYear(payFrequencyCode),
            _         => null
        };

    private static decimal PeriodsPerYear(string freqCode) => freqCode switch
    {
        "WEEKLY"       => 52m,
        "BIWEEKLY"     => 26m,
        "SEMI_MONTHLY" => 24m,
        "MONTHLY"      => 12m,
        "QUARTERLY"    => 4m,
        "ANNUAL"       => 1m,
        _              => 0m
    };
}
