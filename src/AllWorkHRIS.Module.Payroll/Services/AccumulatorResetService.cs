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
        // Audit materialization must never block the run that triggered it.
        try
        {
            var defs = await _accumulatorRepo.GetAllActiveDefinitionsAsync(payDate);
            var now  = _wallClock.UtcNow;

            foreach (var def in defs.Where(d => d.ResetType is "CALENDAR_YEAR" or "PLAN_YEAR"))
            {
                ct.ThrowIfCancellationRequested();

                var b = ResetBoundary.Prior(def.ResetType, def.PlanYearStartMonth, def.PlanYearStartDay, payDate);
                var closings = await _accumulatorRepo.GetClosingBalancesForBoundaryAsync(
                    def.AccumulatorDefinitionId, payrollContextId, b.Start, b.End);

                foreach (var c in closings)
                {
                    if (await _accumulatorRepo.ResetAuditExistsAsync(def.AccumulatorDefinitionId, c.ParticipantId, b.Year))
                        continue;

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
                        OpenedBy                = "SYSTEM",
                        ResetSource             = "AUTOMATIC",
                        Notes                   = null,
                        CreatedBy               = SystemActor,
                        CreationTimestamp       = now
                    });

                    _logger.LogInformation(
                        "Reset audit recorded: {Code} participant {Participant} boundary {Year} closing {Balance:F2}",
                        def.AccumulatorCode, c.ParticipantId, b.Year, c.ClosingBalance);
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Reset detection failed for context {Context} payDate {PayDate}; continuing.",
                payrollContextId, payDate);
        }
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
