using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Payroll.Domain.Accumulators;

namespace AllWorkHRIS.Module.Payroll.Repositories;

public interface IAccumulatorRepository
{
    Task<AccumulatorDefinition?> GetDefinitionByCodeAsync(string accumulatorCode, DateOnly asOf);

    /// <summary>
    /// Resolves the accumulator definition a benefit deduction feeds via the explicit
    /// <c>benefit_deduction.accumulator_definition_id</c> link (NOT by code-string match).
    /// Returns null when the deduction has no link (i.e. it does not accumulate).
    /// </summary>
    Task<AccumulatorDefinition?> GetDefinitionForDeductionAsync(string deductionCode, DateOnly asOf);

    /// <summary>
    /// Resolves the accumulator definition an earnings code feeds via the explicit
    /// <c>earnings_code.accumulator_definition_id</c> link (NOT by code-string match).
    /// Returns null when the earnings code has no link (i.e. it does not accumulate).
    /// The earnings-side twin of <see cref="GetDefinitionForDeductionAsync"/> (Phase 12.5.4).
    /// </summary>
    Task<AccumulatorDefinition?> GetDefinitionForEarningsAsync(string earningsCode, DateOnly asOf);

    Task<IReadOnlyList<AccumulatorDefinition>> GetAllActiveDefinitionsAsync(DateOnly asOf);

    Task<AccumulatorBalance?> GetBalanceAsync(Guid accumulatorDefinitionId, Guid? employmentId,
        Guid? legalEntityId, Guid periodId);

    /// <summary>
    /// Returns YTD balances keyed by accumulator_code for all CALENDAR_YEAR accumulators
    /// with periods that start in the same calendar year as <paramref name="asOf"/> and
    /// before <paramref name="asOf"/> (i.e., prior completed periods only).
    /// </summary>
    Task<IReadOnlyDictionary<string, decimal>> GetYtdBalancesAsync(Guid employmentId, DateOnly asOf);

    Task UpsertBalanceAsync(AccumulatorBalance balance);
    Task InsertImpactAsync(AccumulatorImpact impact);
    Task InsertContributionAsync(AccumulatorContribution contribution);

    /// <summary>
    /// Atomically writes impact + contribution + balance upsert in a single transaction.
    /// </summary>
    Task ApplyImpactChainAsync(AccumulatorImpact impact, AccumulatorContribution contribution,
        AccumulatorBalance balance, IUnitOfWork uow);

    Task<IReadOnlyList<AccumulatorImpact>>       GetImpactsByRunIdAsync(Guid runId);
    Task<IReadOnlyList<AccumulatorImpact>>       GetImpactsByEmploymentIdAsync(Guid employmentId);
    Task<IReadOnlyList<AccumulatorImpact>>       GetImpactsByResultIdAsync(Guid employeePayrollResultId);
    Task<IReadOnlyList<AccumulatorContribution>> GetContributionsByResultIdAsync(Guid employeePayrollResultId);

    /// <summary>
    /// Slim existence check for the approval-time idempotency guard (ADR-017 §2).
    /// Returns true if any accumulator_impact row exists for the given result —
    /// indicating the result has already been posted to the ledger and the
    /// approve-time post should skip it.
    /// </summary>
    Task<bool> AnyImpactsForResultAsync(Guid employeePayrollResultId);

    /// <summary>
    /// Sets current_value on the accumulator_balance row identified by
    /// (definition, participant, period) back to <paramref name="targetValue"/>.
    /// Used exclusively by the reversal path.
    /// </summary>
    Task RevertBalanceAsync(Guid accumulatorDefinitionId, Guid employmentId,
        Guid periodId, decimal targetValue, Guid runId, DateTimeOffset now);

    // ── Period Reset Audit (ADR-020 / Phase 12.9) ──────────────────────────────

    /// <summary>
    /// Idempotency guard for reset materialization: true if a reset row already
    /// exists for this (definition, participant, boundary year).
    /// </summary>
    Task<bool> ResetAuditExistsAsync(Guid accumulatorDefinitionId, Guid? participantId, int resetBoundaryYear);

    /// <summary>The recorded closing balance for a (definition, participant, boundary), or null if none.
    /// Used to decide insert vs. amend on a close pass (ADR-022 §D5 / Phase 12.10.3).</summary>
    Task<decimal?> GetResetAuditClosingAsync(Guid accumulatorDefinitionId, Guid? participantId, int resetBoundaryYear);

    /// <summary>Amends an existing reset record's closing balance + notes after a post-close (W-2c)
    /// adjustment changes the boundary's total (ADR-022 §D5 / Phase 12.10.3). Audit-only.</summary>
    Task UpdateResetAuditClosingAsync(Guid accumulatorDefinitionId, Guid? participantId, int resetBoundaryYear, decimal closingBalance, string? notes);

    /// <summary>Inserts one audit-only accumulator reset record.</summary>
    Task InsertResetAuditAsync(AccumulatorResetAudit audit);

    /// <summary>
    /// Closing balances for one reset-eligible definition at a boundary, per participant
    /// across the whole <b>legal entity</b> (ADR-022 / Phase 12.10.2 — entity-wide, not
    /// per-context): SUM(accumulator_balance.current_value) over the periods that fall in
    /// [boundaryStart, boundaryEnd] — by <c>pay_date</c> for PAY_DATE accumulators
    /// (constructive receipt), by period dates for WORK_PERIOD. Participants with a
    /// zero/absent balance are omitted.
    /// </summary>
    Task<IReadOnlyList<ResetClosingBalance>> GetClosingBalancesForBoundaryAsync(
        Guid accumulatorDefinitionId, Guid legalEntityId, DateOnly boundaryStart, DateOnly boundaryEnd);

    /// Resolves the legal entity that owns a payroll context (used to scope the
    /// entity-wide year-close from the run that triggered it).
    Task<Guid?> GetLegalEntityIdForContextAsync(Guid payrollContextId);
}

/// <summary>Per-participant closing balance for a reset boundary (ADR-020 / Phase 12.9).</summary>
public sealed record ResetClosingBalance(Guid ParticipantId, Guid? LegalEntityId, decimal ClosingBalance);
