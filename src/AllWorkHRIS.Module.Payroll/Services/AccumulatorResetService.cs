using AllWorkHRIS.Core.Temporal;
using AllWorkHRIS.Module.Payroll.Domain.Accumulators;
using AllWorkHRIS.Module.Payroll.Repositories;
using Microsoft.Extensions.Logging;

namespace AllWorkHRIS.Module.Payroll.Services;

/// <summary>
/// Period Reset Audit (ADR-020 / Phase 12.9). Audit-only — records reset snapshots;
/// balances are never mutated (YTD is derived, so a new boundary starts fresh).
/// </summary>
public sealed class AccumulatorResetService : IAccumulatorResetService
{
    // System actor for automatic resets (matches the seed system user).
    private static readonly Guid SystemActor = new("00000000-0000-0000-0000-000000000001");

    private readonly IAccumulatorRepository _accumulatorRepo;
    private readonly IWallClock             _wallClock;
    private readonly ILogger<AccumulatorResetService> _logger;

    public AccumulatorResetService(
        IAccumulatorRepository accumulatorRepo, IWallClock wallClock, ILogger<AccumulatorResetService> logger)
    {
        _accumulatorRepo = accumulatorRepo;
        _wallClock       = wallClock;
        _logger          = logger;
    }

    public async Task DetectAndRecordResetsAsync(Guid payrollContextId, DateOnly payDate, CancellationToken ct = default)
    {
        // Automatic, run-adjacent trigger (ADR-022 §D3): when a run's pay date crosses a
        // tax-year boundary, close the prior boundary ENTITY-WIDE — every participant in
        // the run's legal entity, not just its pay group — so the close is definitive in a
        // single pass (no per-context running total). Must never block the triggering run.
        try
        {
            var legalEntityId = await _accumulatorRepo.GetLegalEntityIdForContextAsync(payrollContextId);
            if (legalEntityId is null)
            {
                _logger.LogWarning("Reset detection: no legal entity for context {Context}; skipping.", payrollContextId);
                return;
            }
            await CloseBoundaryAsync(legalEntityId.Value, payDate, "SYSTEM", "AUTOMATIC", ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Reset detection failed for context {Context} payDate {PayDate}; continuing.",
                payrollContextId, payDate);
        }
    }

    public async Task<int> CloseEntityYearAsync(Guid legalEntityId, int boundaryYear, string actor, CancellationToken ct = default)
    {
        // Manual, operator-triggered entity-wide close (ADR-022 §D3, dual-trigger): records
        // the entity's closing snapshot for boundaryYear (opened_by = actor, MANUAL).
        // Idempotent — boundaries already closed (e.g. by the automatic trigger) are skipped.
        // A pay date at the start of the FOLLOWING year selects boundaryYear as the prior boundary.
        // Returns the number of newly-recorded reset rows.
        var justAfterBoundary = new DateOnly(boundaryYear + 1, 1, 1);
        return await CloseBoundaryAsync(legalEntityId, justAfterBoundary, actor, "MANUAL", ct);
    }

    // Shared entity-wide close: for each reset-eligible accumulator, materialize the
    // just-closed boundary's per-participant closing snapshots across the whole legal
    // entity, idempotent per (definition, participant, boundary). Returns rows written.
    private async Task<int> CloseBoundaryAsync(
        Guid legalEntityId, DateOnly payDate, string openedBy, string source, CancellationToken ct)
    {
        var defs    = await _accumulatorRepo.GetAllActiveDefinitionsAsync(payDate);
        var now     = _wallClock.UtcNow;
        var touched = 0;

        foreach (var def in defs.Where(d => d.ResetType is "CALENDAR_YEAR" or "PLAN_YEAR"))
        {
            ct.ThrowIfCancellationRequested();

            var b = ResetBoundary.Prior(def.ResetType, def.PlanYearStartMonth, def.PlanYearStartDay, payDate);
            var closings = await _accumulatorRepo.GetClosingBalancesForBoundaryAsync(
                def.AccumulatorDefinitionId, legalEntityId, b.Start, b.End);

            foreach (var c in closings)
            {
                // Insert if new; amend if a post-close (W-2c) adjustment changed the closing
                // (ADR-022 §D5 / Phase 12.10.3); skip if already recorded and unchanged.
                var existing = await _accumulatorRepo.GetResetAuditClosingAsync(
                    def.AccumulatorDefinitionId, c.ParticipantId, b.Year);

                if (existing is null)
                {
                    await _accumulatorRepo.InsertResetAuditAsync(new AccumulatorResetAudit
                    {
                        AccumulatorResetAuditId = Guid.NewGuid(),
                        AccumulatorDefinitionId = def.AccumulatorDefinitionId,
                        AccumulatorFamilyId     = def.AccumulatorFamilyId,
                        ParticipantId           = c.ParticipantId,
                        LegalEntityId           = c.LegalEntityId,
                        ScopeTypeId             = def.ScopeTypeId,
                        ResetType               = def.ResetType,
                        ResetBoundaryYear       = b.Year,
                        ResetDate               = b.ResetDate,
                        ClosingBalance          = c.ClosingBalance,
                        OpenedBy                = openedBy,
                        ResetSource             = source,
                        Notes                   = null,
                        CreatedBy               = SystemActor,
                        CreationTimestamp       = now
                    });

                    touched++;
                    _logger.LogInformation(
                        "Reset audit recorded ({Source}): {Code} participant {Participant} boundary {Year} closing {Balance:F2}",
                        source, def.AccumulatorCode, c.ParticipantId, b.Year, c.ClosingBalance);
                }
                else if (existing.Value != c.ClosingBalance)
                {
                    await _accumulatorRepo.UpdateResetAuditClosingAsync(
                        def.AccumulatorDefinitionId, c.ParticipantId, b.Year, c.ClosingBalance,
                        $"Amended (post-close adjustment); prior closing {existing.Value:F2}");

                    touched++;
                    _logger.LogInformation(
                        "Reset audit amended ({Source}): {Code} participant {Participant} boundary {Year} {Old:F2} -> {New:F2}",
                        source, def.AccumulatorCode, c.ParticipantId, b.Year, existing.Value, c.ClosingBalance);
                }
                // else: already recorded and unchanged — skip.
            }
        }

        return touched;
    }

    public async Task RecordManualResetAsync(AccumulatorDefinition definition, Guid participantId, Guid? legalEntityId,
        int boundaryYear, DateOnly resetDate, decimal closingBalance, Guid actor, string? notes,
        CancellationToken ct = default)
    {
        if (await _accumulatorRepo.ResetAuditExistsAsync(definition.AccumulatorDefinitionId, participantId, boundaryYear))
            throw new InvalidOperationException(
                $"A reset is already recorded for {definition.AccumulatorCode}, participant {participantId}, boundary {boundaryYear}.");

        await _accumulatorRepo.InsertResetAuditAsync(new AccumulatorResetAudit
        {
            AccumulatorResetAuditId = Guid.NewGuid(),
            AccumulatorDefinitionId = definition.AccumulatorDefinitionId,
            AccumulatorFamilyId     = definition.AccumulatorFamilyId,
            ParticipantId           = participantId,
            LegalEntityId           = legalEntityId,
            ScopeTypeId             = definition.ScopeTypeId,
            ResetType               = definition.ResetType,
            ResetBoundaryYear       = boundaryYear,
            ResetDate               = resetDate,
            ClosingBalance          = closingBalance,
            OpenedBy                = actor.ToString(),
            ResetSource             = "MANUAL",
            Notes                   = notes,
            CreatedBy               = actor,
            CreationTimestamp       = _wallClock.UtcNow
        });
    }

}
