using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Core.Temporal;
using AllWorkHRIS.Module.Payroll.Domain.Accumulators;
using AllWorkHRIS.Module.Payroll.Domain.Results;
using AllWorkHRIS.Module.Payroll.Repositories;

namespace AllWorkHRIS.Module.Payroll.Services;

public sealed class AccumulatorService : IAccumulatorService
{
    private readonly IAccumulatorRepository _accumulatorRepo;
    private readonly IResultLineRepository  _resultLineRepo;
    private readonly ITemporalContext       _temporalContext;
    private readonly IConnectionFactory     _connectionFactory;
    private readonly ILookupCache           _lookup;

    public AccumulatorService(IAccumulatorRepository accumulatorRepo, IResultLineRepository resultLineRepo,
        ITemporalContext temporalContext, IConnectionFactory connectionFactory, ILookupCache lookup)
    {
        _accumulatorRepo   = accumulatorRepo;
        _resultLineRepo    = resultLineRepo;
        _temporalContext   = temporalContext;
        _connectionFactory = connectionFactory;
        _lookup            = lookup;
    }

    // Lookup ids resolved by code (Phase 12.5.8) — never hard-coded literals, so a seed
    // re-order can't silently mislabel impact/contribution/balance rows.
    private int Lkp(string table, string code) => _lookup.GetId(table, code);

    public async Task ApplyAsync(EmployeePayrollResult result, Guid runId, CancellationToken ct = default)
    {
        var now  = _temporalContext.GetOperativeNow();
        var asOf = DateOnly.FromDateTime(_temporalContext.GetOperativeDate());

        var earningsLines     = await _resultLineRepo.GetEarningsByResultIdAsync(result.EmployeePayrollResultId);
        var deductionLines    = await _resultLineRepo.GetDeductionsByResultIdAsync(result.EmployeePayrollResultId);
        var taxLines          = await _resultLineRepo.GetTaxLinesByResultIdAsync(result.EmployeePayrollResultId);
        var contributionLines = await _resultLineRepo.GetContributionsByResultIdAsync(result.EmployeePayrollResultId);

        // Single transaction for the entire employee — any mid-chain failure rolls back all layers.
        using var uow = new UnitOfWork(_connectionFactory);

        // 0-based ordinal across every impact this apply posts (earnings → deductions →
        // taxes → contributions). All share `now`, so this is the impact-trail tiebreaker
        // for a stable running balance (Phase 12.5.5).
        var sequence = 0;

        foreach (var line in earningsLines.Where(l => l.AccumulatorImpactFlag))
        {
            // Resolve the accumulator via the explicit earnings_code link, not by
            // matching the earnings code to an accumulator_code string (the prior
            // fragile coupling — same silent-skip class 029 closed for deductions).
            // No link => the earnings code does not accumulate (Phase 12.5.4).
            var def = await _accumulatorRepo.GetDefinitionForEarningsAsync(line.EarningsCode, asOf);
            if (def is null) continue;
            await ApplyChainAsync(def, line.CalculatedAmount, line.EarningsResultLineId, "EARNINGS_LINE", result, runId, now, sequence++, uow);
            ct.ThrowIfCancellationRequested();
        }

        foreach (var line in deductionLines.Where(l => l.AccumulatorImpactFlag))
        {
            // Resolve the accumulator via the explicit benefit_deduction link, not by
            // matching the deduction code to an accumulator_code string (the prior fragile
            // coupling that silently dropped e.g. Roth 401k when the codes differed).
            // No link => the deduction does not accumulate.
            var def = await _accumulatorRepo.GetDefinitionForDeductionAsync(line.DeductionCode, asOf);
            if (def is null) continue;
            await ApplyChainAsync(def, line.CalculatedAmount, line.DeductionResultLineId, "DEDUCTION_LINE", result, runId, now, sequence++, uow);
            ct.ThrowIfCancellationRequested();
        }

        foreach (var line in taxLines.Where(l => l.AccumulatorImpactFlag))
        {
            var def = await _accumulatorRepo.GetDefinitionByCodeAsync(line.TaxCode, asOf);
            if (def is null) continue;
            await ApplyChainAsync(def, line.CalculatedAmount, line.TaxResultLineId, "TAX_LINE", result, runId, now, sequence++, uow);
            ct.ThrowIfCancellationRequested();
        }

        foreach (var line in contributionLines.Where(l => l.AccumulatorImpactFlag))
        {
            var def = await _accumulatorRepo.GetDefinitionByCodeAsync(line.ContributionCode, asOf);
            if (def is null) continue;
            await ApplyChainAsync(def, line.CalculatedAmount, line.EmployerContributionResultLineId, "EMPLOYER_CONTRIBUTION_LINE", result, runId, now, sequence++, uow);
            ct.ThrowIfCancellationRequested();
        }

        uow.Commit();
    }

    public async Task ReverseAsync(Guid employeePayrollResultId, Guid reversedBy, CancellationToken ct = default)
    {
        var impacts = await _accumulatorRepo.GetImpactsByResultIdAsync(employeePayrollResultId);
        if (impacts.Count == 0) return;

        var now = _temporalContext.GetOperativeNow();

        // Insert a negating impact row for every original impact, linking back to it.
        foreach (var original in impacts)
        {
            var reversal = new AccumulatorImpact
            {
                AccumulatorImpactId      = Guid.NewGuid(),
                AccumulatorDefinitionId  = original.AccumulatorDefinitionId,
                PayrollRunResultSetId    = original.PayrollRunResultSetId,
                EmployeePayrollResultId  = original.EmployeePayrollResultId,
                PayrollRunId             = original.PayrollRunId,
                EmploymentId             = original.EmploymentId,
                PersonId                 = original.PersonId,
                ImpactStatusId           = Lkp(LookupTables.ImpactStatus, "POSTED"),
                ImpactSourceTypeId       = original.ImpactSourceTypeId,
                SourceObjectId           = original.SourceObjectId,
                PriorValue               = original.NewValue,
                DeltaValue               = -original.DeltaValue,
                NewValue                 = original.PriorValue,
                PostingDirectionId       = Lkp(LookupTables.PostingDirection, original.DeltaValue >= 0 ? "DECREASE" : "INCREASE"),   // flip direction
                ScopeTypeId              = original.ScopeTypeId,
                ScopeObjectId            = original.ScopeObjectId,
                JurisdictionId           = original.JurisdictionId,
                RulePackId               = null,
                RuleVersionId            = null,
                RetroactiveFlag          = false,
                ReversalFlag             = true,
                CorrectionFlag           = false,
                PriorAccumulatorImpactId = original.AccumulatorImpactId,
                Notes                    = $"Reversal of run cancellation by {reversedBy}",
                ImpactTimestamp          = now,
                CreatedTimestamp         = now,
                UpdatedTimestamp         = now
            };
            await _accumulatorRepo.InsertImpactAsync(reversal);
            ct.ThrowIfCancellationRequested();
        }

        // For each unique accumulator definition, revert the balance to the pre-run value.
        // impacts are ordered ASC — the first impact per definition has the correct PriorValue.
        var executionPeriodId = await ResolveExecutionPeriodIdAsync(employeePayrollResultId);
        if (executionPeriodId is null) return;

        var firstImpactPerDef = impacts
            .GroupBy(i => i.AccumulatorDefinitionId)
            .Select(g => g.First());

        foreach (var impact in firstImpactPerDef)
        {
            if (impact.EmploymentId is null) continue;
            await _accumulatorRepo.RevertBalanceAsync(
                impact.AccumulatorDefinitionId,
                impact.EmploymentId.Value,
                executionPeriodId.Value,
                impact.PriorValue,
                impact.PayrollRunId,
                now);
        }
    }

    private async Task<Guid?> ResolveExecutionPeriodIdAsync(Guid employeePayrollResultId)
    {
        // The result line repository doesn't expose the result header directly, so we
        // derive the period from any impact row's contribution record via the run context.
        // Simplest path: read it from the first impact's run → period link isn't stored on
        // the impact itself, so we lean on the contribution table which carries it.
        // For now we query accumulator_contribution for the result.
        var contributions = await _accumulatorRepo.GetContributionsByResultIdAsync(employeePayrollResultId);
        return contributions.Count > 0 ? contributions[0].ExecutionPeriodId : null;
    }

    public Task<IReadOnlyDictionary<string, decimal>> GetYtdBalancesAsync(Guid employmentId, DateOnly asOf)
        => _accumulatorRepo.GetYtdBalancesAsync(employmentId, asOf);

    // -------------------------------------------------------
    // Four-layer accumulator mutation chain (per SPEC §9):
    //   Impact → Contribution → Balance
    // The current balance is read first so prior value is
    // captured accurately in the Impact and Contribution rows.
    // -------------------------------------------------------

    private async Task ApplyChainAsync(
        AccumulatorDefinition def, decimal delta, Guid sourceLineId, string sourceTypeCode,
        EmployeePayrollResult result, Guid runId, DateTimeOffset now, int sequence, IUnitOfWork uow)
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
            ImpactStatusId           = Lkp(LookupTables.ImpactStatus, "POSTED"),
            ImpactSourceTypeId       = Lkp(LookupTables.ImpactSourceType, sourceTypeCode),
            SourceObjectId           = sourceLineId,
            PriorValue               = priorValue,
            DeltaValue               = delta,
            NewValue                 = newValue,
            PostingDirectionId       = Lkp(LookupTables.PostingDirection, delta >= 0 ? "INCREASE" : "DECREASE"),
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
            ApplySequence            = sequence,
            ImpactTimestamp          = now,
            CreatedTimestamp         = now,
            UpdatedTimestamp         = now
        };
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
            ContributionTypeId          = Lkp(LookupTables.ContributionType, "ORIGINAL"),
            BeforeValue                 = priorValue,
            AfterValue                  = newValue,
            CreationTimestamp           = now
        };
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
            BalanceStatusId          = Lkp(LookupTables.AccumulatorBalanceStatus, "ACTIVE"),
            LastUpdatedRunId         = runId,
            LastUpdatedResultSetId   = null,
            LastUpdateTimestamp      = now
        };
        await _accumulatorRepo.ApplyImpactChainAsync(impact, contribution, updatedBalance, uow);
    }
}
