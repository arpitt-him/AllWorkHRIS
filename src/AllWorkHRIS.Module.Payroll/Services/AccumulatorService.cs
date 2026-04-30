using AllWorkHRIS.Module.Payroll.Domain.Accumulators;
using AllWorkHRIS.Module.Payroll.Domain.Results;
using AllWorkHRIS.Module.Payroll.Repositories;

namespace AllWorkHRIS.Module.Payroll.Services;

public sealed class AccumulatorService : IAccumulatorService
{
    private readonly IAccumulatorRepository _accumulatorRepo;
    private readonly IResultLineRepository  _resultLineRepo;

    public AccumulatorService(IAccumulatorRepository accumulatorRepo, IResultLineRepository resultLineRepo)
    {
        _accumulatorRepo = accumulatorRepo;
        _resultLineRepo  = resultLineRepo;
    }

    public async Task ApplyAsync(EmployeePayrollResult result, Guid runId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var earningsLines     = await _resultLineRepo.GetEarningsByResultIdAsync(result.EmployeePayrollResultId);
        var deductionLines    = await _resultLineRepo.GetDeductionsByResultIdAsync(result.EmployeePayrollResultId);
        var taxLines          = await _resultLineRepo.GetTaxLinesByResultIdAsync(result.EmployeePayrollResultId);
        var contributionLines = await _resultLineRepo.GetContributionsByResultIdAsync(result.EmployeePayrollResultId);

        foreach (var line in earningsLines.Where(l => l.AccumulatorImpactFlag))
        {
            var def = await _accumulatorRepo.GetDefinitionByCodeAsync(line.EarningsCode);
            if (def is null) continue;
            await ApplyChainAsync(def, line.CalculatedAmount, line.EarningsResultLineId, result, runId, now);
            ct.ThrowIfCancellationRequested();
        }

        foreach (var line in deductionLines.Where(l => l.AccumulatorImpactFlag))
        {
            var def = await _accumulatorRepo.GetDefinitionByCodeAsync(line.DeductionCode);
            if (def is null) continue;
            await ApplyChainAsync(def, line.CalculatedAmount, line.DeductionResultLineId, result, runId, now);
            ct.ThrowIfCancellationRequested();
        }

        foreach (var line in taxLines.Where(l => l.AccumulatorImpactFlag))
        {
            var def = await _accumulatorRepo.GetDefinitionByCodeAsync(line.TaxCode);
            if (def is null) continue;
            await ApplyChainAsync(def, line.CalculatedAmount, line.TaxResultLineId, result, runId, now);
            ct.ThrowIfCancellationRequested();
        }

        foreach (var line in contributionLines.Where(l => l.AccumulatorImpactFlag))
        {
            var def = await _accumulatorRepo.GetDefinitionByCodeAsync(line.ContributionCode);
            if (def is null) continue;
            await ApplyChainAsync(def, line.CalculatedAmount, line.EmployerContributionResultLineId, result, runId, now);
            ct.ThrowIfCancellationRequested();
        }
    }

    public Task ReverseAsync(Guid employeePayrollResultId, Guid reversedBy, CancellationToken ct = default)
        => throw new NotImplementedException("Accumulator reversal is deferred to the correction run flow (Phase 4.6+).");

    // -------------------------------------------------------
    // Four-layer accumulator mutation chain (per SPEC §9):
    //   Impact → Contribution → Balance
    // The current balance is read first so prior value is
    // captured accurately in the Impact and Contribution rows.
    // -------------------------------------------------------

    private async Task ApplyChainAsync(
        AccumulatorDefinition def, decimal delta, Guid sourceLineId,
        EmployeePayrollResult result, Guid runId, DateTimeOffset now)
    {
        // Read current balance — if absent this is the first contribution for this scope/period
        var existingBalance = await _accumulatorRepo.GetBalanceAsync(
            def.AccumulatorDefinitionId, result.EmploymentId, null, result.ExecutionPeriodId);

        var accumulatorId = existingBalance?.AccumulatorId ?? Guid.NewGuid();
        var priorValue    = existingBalance?.CurrentValue  ?? 0m;
        var newValue      = priorValue + delta;

        // Layer 2 — AccumulatorImpact (mutation event record)
        var impact = new AccumulatorImpact
        {
            AccumulatorImpactId      = Guid.NewGuid(),
            AccumulatorDefinitionId  = def.AccumulatorDefinitionId,
            PayrollRunResultSetId    = null,
            EmployeePayrollResultId  = result.EmployeePayrollResultId,
            PayrollRunId             = runId,
            EmploymentId             = result.EmploymentId,
            PersonId                 = result.PersonId,
            ImpactStatusId           = 1,             // POSTED
            ImpactSourceTypeId       = 1,             // CALCULATION
            SourceObjectId           = sourceLineId,
            PriorValue               = priorValue,
            DeltaValue               = delta,
            NewValue                 = newValue,
            PostingDirectionId       = delta >= 0 ? 1 : 2,   // CREDIT : DEBIT
            ScopeTypeId              = def.ScopeTypeId,
            ScopeObjectId            = result.EmploymentId,
            JurisdictionId           = null,
            RulePackId               = null,
            RuleVersionId            = null,
            RetroactiveFlag          = false,
            ReversalFlag             = false,
            CorrectionFlag           = false,
            PriorAccumulatorImpactId = null,
            Notes                    = null,
            ImpactTimestamp          = now,
            CreatedTimestamp         = now,
            UpdatedTimestamp         = now
        };
        await _accumulatorRepo.InsertImpactAsync(impact);

        // Layer 3 — AccumulatorContribution (persisted history)
        var contribution = new AccumulatorContribution
        {
            ContributionId              = Guid.NewGuid(),
            AccumulatorId               = accumulatorId,
            AccumulatorImpactId         = impact.AccumulatorImpactId,
            AccumulatorDefinitionId     = def.AccumulatorDefinitionId,
            ParentContributionId        = null,
            RootContributionId          = null,
            ContributionLineageSequence = 1,
            CorrectionReferenceId       = null,
            SourceRunId                 = runId,
            SourceResultSetId           = null,
            SourceEmployeeResultId      = result.EmployeePayrollResultId,
            SourcePeriodId              = result.SourcePeriodId,
            ExecutionPeriodId           = result.ExecutionPeriodId,
            EmploymentId                = result.EmploymentId,
            ScopeTypeId                 = def.ScopeTypeId,
            ScopeObjectId               = result.EmploymentId,
            ContributionAmount          = delta,
            ContributionTypeId          = 1,      // STANDARD
            BeforeValue                 = priorValue,
            AfterValue                  = newValue,
            ContributionTimestamp       = now,
            CreatedTimestamp            = now
        };
        await _accumulatorRepo.InsertContributionAsync(contribution);

        // Layer 4 — AccumulatorBalance (authoritative current state — upsert)
        var updatedBalance = new AccumulatorBalance
        {
            AccumulatorId            = accumulatorId,
            AccumulatorDefinitionId  = def.AccumulatorDefinitionId,
            AccumulatorFamilyId      = def.AccumulatorFamilyId,
            ScopeTypeId              = def.ScopeTypeId,
            ParticipantId            = result.EmploymentId,
            EmployerId               = null,
            JurisdictionId           = null,
            PlanId                   = null,
            PeriodContextId          = def.PeriodContextId,
            CalendarContextId        = result.ExecutionPeriodId,
            CurrentValue             = newValue,
            BalanceStatusId          = 1,     // ACTIVE
            LastUpdatedRunId         = runId,
            LastUpdatedResultSetId   = null,
            LastUpdateTimestamp      = now
        };
        await _accumulatorRepo.UpsertBalanceAsync(updatedBalance);
    }
}
