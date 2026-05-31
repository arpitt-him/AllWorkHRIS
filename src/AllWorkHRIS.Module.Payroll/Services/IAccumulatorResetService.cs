using AllWorkHRIS.Module.Payroll.Domain.Accumulators;

namespace AllWorkHRIS.Module.Payroll.Services;

/// <summary>
/// Period Reset Audit (ADR-020 / Phase 12.9). Audit-only: records closing-balance
/// snapshots at reset boundaries; never mutates balances.
/// </summary>
public interface IAccumulatorResetService
{
    /// <summary>
    /// Automatic, idempotent reset detection — called when a run commits (approval).
    /// For each reset-eligible accumulator, if the boundary just before <paramref name="payDate"/>
    /// has un-audited closing balances for participants in the context, records them
    /// (<c>opened_by = SYSTEM</c>). reset_type-aware (calendar and plan-year). Never throws.
    /// ADR-022: closes the prior boundary <b>entity-wide</b> (all pay groups in the run's
    /// legal entity), on a pay-date basis, so the close is definitive in a single pass.
    /// </summary>
    Task DetectAndRecordResetsAsync(Guid payrollContextId, DateOnly payDate, CancellationToken ct = default);

    /// <summary>
    /// Manual, operator-triggered <b>entity-wide</b> year-close (ADR-022 §D3, dual-trigger):
    /// records the closing snapshot for <paramref name="boundaryYear"/> across every
    /// participant in the legal entity (<c>opened_by</c> = the actor, <c>reset_source = MANUAL</c>).
    /// Idempotent — boundaries already closed (e.g. by the automatic trigger) are skipped.
    /// </summary>
    Task CloseEntityYearAsync(Guid legalEntityId, int boundaryYear, Guid actor, CancellationToken ct = default);

    /// <summary>
    /// Manual reset record (off-cycle / correction) — <c>opened_by</c> = the actor,
    /// <c>reset_source = MANUAL</c>. Service-only in v1 (no UI yet). Throws if a reset
    /// already exists for the (definition, participant, boundary).
    /// </summary>
    Task RecordManualResetAsync(AccumulatorDefinition definition, Guid participantId, Guid? legalEntityId,
        int boundaryYear, DateOnly resetDate, decimal closingBalance, Guid actor, string? notes,
        CancellationToken ct = default);
}
