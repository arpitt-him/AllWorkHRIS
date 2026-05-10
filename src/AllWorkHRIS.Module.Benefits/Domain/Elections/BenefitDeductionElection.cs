using AllWorkHRIS.Module.Benefits.Commands;

namespace AllWorkHRIS.Module.Benefits.Domain.Elections;


public sealed record BenefitDeductionElection
{
    public Guid             ElectionId                  { get; init; }
    public Guid             EmploymentId                { get; init; }
    public Guid             DeductionId                 { get; init; }
    public string           DeductionCode               { get; init; } = string.Empty;  // populated by JOIN; not a DB column
    public string           CalculationMode             { get; init; } = string.Empty;  // populated by JOIN; not a DB column
    public string           TaxTreatment                { get; init; } = string.Empty;
    public decimal          EmployeeAmount              { get; init; }
    public decimal?         EmployerContributionAmount  { get; init; }
    public decimal?         ContributionPct             { get; init; }
    public decimal?         EmployerContributionPct     { get; init; }
    public string?          CoverageTier                { get; init; }
    public decimal?         AnnualCoverageAmount        { get; init; }
    public decimal?         AnnualElectionAmount        { get; init; }
    public decimal?         MonthlyElectionAmount       { get; init; }
    public DateOnly         EffectiveStartDate          { get; init; }
    public DateOnly?        EffectiveEndDate            { get; init; }
    public string           Status                      { get; init; } = string.Empty;
    public string           Source                      { get; init; } = string.Empty;
    public string           CreatedBy                   { get; init; } = string.Empty;
    public DateTimeOffset   CreatedAt                   { get; init; }
    public DateTimeOffset   UpdatedAt                   { get; init; }
    public Guid             ElectionVersionId           { get; init; }
    public Guid?            OriginalElectionId          { get; init; }
    public Guid?            ParentElectionId            { get; init; }
    public string?          CorrectionType              { get; init; }
    public Guid?            SourceEventId               { get; init; }

    public static BenefitDeductionElection Create(
        CreateElectionCommand cmd, string taxTreatment, string deductionCode)
    {
        var now    = DateTimeOffset.UtcNow;
        var today  = DateOnly.FromDateTime(now.UtcDateTime);
        var status = cmd.EffectiveStartDate <= today ? ElectionStatus.Active : ElectionStatus.Pending;
        return new BenefitDeductionElection
        {
            ElectionId                 = Guid.NewGuid(),
            EmploymentId               = cmd.EmploymentId,
            DeductionId                = cmd.DeductionId,
            DeductionCode              = deductionCode,
            TaxTreatment               = taxTreatment,
            EmployeeAmount             = cmd.EmployeeAmount ?? 0,
            EmployerContributionAmount = cmd.EmployerContributionAmount,
            ContributionPct            = cmd.ContributionPct,
            EmployerContributionPct    = cmd.EmployerContributionPct,
            CoverageTier               = cmd.CoverageTier,
            AnnualCoverageAmount       = cmd.AnnualCoverageAmount,
            EffectiveStartDate         = cmd.EffectiveStartDate,
            EffectiveEndDate           = cmd.EffectiveEndDate,
            Status                     = status,
            Source                     = cmd.Source,
            CreatedBy                  = cmd.CreatedBy.ToString(),
            CreatedAt                  = now,
            UpdatedAt                  = now,
            ElectionVersionId          = Guid.NewGuid()
        };
    }

    public static BenefitDeductionElection CreateRevision(
        BenefitDeductionElection prior, UpdateElectionCommand cmd)
    {
        var now    = DateTimeOffset.UtcNow;
        var today  = DateOnly.FromDateTime(now.UtcDateTime);
        var status = cmd.EffectiveStartDate <= today ? ElectionStatus.Active : ElectionStatus.Pending;
        return new BenefitDeductionElection
        {
            ElectionId                 = Guid.NewGuid(),
            EmploymentId               = prior.EmploymentId,
            DeductionId                = prior.DeductionId,
            DeductionCode              = prior.DeductionCode,
            TaxTreatment               = prior.TaxTreatment,
            EmployeeAmount             = cmd.EmployeeAmount,
            EmployerContributionAmount = cmd.EmployerContributionAmount,
            ContributionPct            = prior.ContributionPct,
            EmployerContributionPct    = prior.EmployerContributionPct,
            CoverageTier               = prior.CoverageTier,
            AnnualCoverageAmount       = prior.AnnualCoverageAmount,
            EffectiveStartDate         = cmd.EffectiveStartDate,
            EffectiveEndDate           = cmd.EffectiveEndDate,
            Status                     = status,
            Source                     = prior.Source,
            CreatedBy                  = cmd.UpdatedBy.ToString(),
            CreatedAt                  = now,
            UpdatedAt                  = now,
            ElectionVersionId          = Guid.NewGuid(),
            OriginalElectionId         = prior.OriginalElectionId ?? prior.ElectionId,
            ParentElectionId           = prior.ElectionId,
            CorrectionType             = cmd.CorrectionType
        };
    }

    // Prospective amendment: new election starts at AmendmentDate; prior is trimmed and superseded.
    // Null values in cmd carry forward from prior.
    public static BenefitDeductionElection CreateAmendment(
        BenefitDeductionElection prior, AmendElectionCommand cmd)
    {
        var now    = DateTimeOffset.UtcNow;
        var today  = DateOnly.FromDateTime(now.UtcDateTime);
        var status = cmd.AmendmentDate <= today ? ElectionStatus.Active : ElectionStatus.Pending;
        return new BenefitDeductionElection
        {
            ElectionId                 = Guid.NewGuid(),
            EmploymentId               = prior.EmploymentId,
            DeductionId                = prior.DeductionId,
            DeductionCode              = prior.DeductionCode,
            TaxTreatment               = prior.TaxTreatment,
            EmployeeAmount             = cmd.EmployeeAmount             ?? prior.EmployeeAmount,
            EmployerContributionAmount = cmd.EmployerContributionAmount ?? prior.EmployerContributionAmount,
            ContributionPct            = cmd.ContributionPct            ?? prior.ContributionPct,
            EmployerContributionPct    = cmd.EmployerContributionPct    ?? prior.EmployerContributionPct,
            CoverageTier               = cmd.CoverageTier               ?? prior.CoverageTier,
            AnnualCoverageAmount       = cmd.AnnualCoverageAmount       ?? prior.AnnualCoverageAmount,
            EffectiveStartDate         = cmd.AmendmentDate,
            EffectiveEndDate           = prior.EffectiveEndDate,
            Status                     = status,
            Source                     = prior.Source,
            CreatedBy                  = cmd.AmendedBy.ToString(),
            CreatedAt                  = now,
            UpdatedAt                  = now,
            ElectionVersionId          = Guid.NewGuid(),
            OriginalElectionId         = prior.OriginalElectionId ?? prior.ElectionId,
            ParentElectionId           = prior.ElectionId
        };
    }

    // Retroactive correction: supersedes prior and inserts replacement with corrected data.
    // Null values in cmd carry forward from prior.
    public static BenefitDeductionElection CreateCorrection(
        BenefitDeductionElection prior, CorrectElectionCommand cmd)
    {
        var now    = DateTimeOffset.UtcNow;
        var today  = DateOnly.FromDateTime(now.UtcDateTime);
        var start  = cmd.EffectiveStartDate ?? prior.EffectiveStartDate;
        var status = start <= today ? ElectionStatus.Active : ElectionStatus.Pending;
        return new BenefitDeductionElection
        {
            ElectionId                 = Guid.NewGuid(),
            EmploymentId               = prior.EmploymentId,
            DeductionId                = prior.DeductionId,
            DeductionCode              = prior.DeductionCode,
            TaxTreatment               = prior.TaxTreatment,
            EmployeeAmount             = cmd.EmployeeAmount             ?? prior.EmployeeAmount,
            EmployerContributionAmount = cmd.EmployerContributionAmount ?? prior.EmployerContributionAmount,
            ContributionPct            = cmd.ContributionPct            ?? prior.ContributionPct,
            EmployerContributionPct    = cmd.EmployerContributionPct    ?? prior.EmployerContributionPct,
            CoverageTier               = cmd.CoverageTier               ?? prior.CoverageTier,
            AnnualCoverageAmount       = cmd.AnnualCoverageAmount       ?? prior.AnnualCoverageAmount,
            EffectiveStartDate         = start,
            EffectiveEndDate           = cmd.EffectiveEndDate ?? prior.EffectiveEndDate,
            Status                     = status,
            Source                     = prior.Source,
            CreatedBy                  = cmd.CorrectedBy.ToString(),
            CreatedAt                  = now,
            UpdatedAt                  = now,
            ElectionVersionId          = Guid.NewGuid(),
            OriginalElectionId         = prior.OriginalElectionId ?? prior.ElectionId,
            ParentElectionId           = prior.ElectionId,
            CorrectionType             = cmd.CorrectionType
        };
    }
}
