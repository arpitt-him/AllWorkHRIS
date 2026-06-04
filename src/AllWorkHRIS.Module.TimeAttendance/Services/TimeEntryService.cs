using AllWorkHRIS.Core.Composition;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Core.Temporal;
using AllWorkHRIS.Module.TimeAttendance.Commands;
using AllWorkHRIS.Module.TimeAttendance.Domain;
using AllWorkHRIS.Module.TimeAttendance.Repositories;
using Microsoft.Extensions.Logging;

namespace AllWorkHRIS.Module.TimeAttendance.Services;

public sealed class TimeEntryService : ITimeEntryService
{
    private readonly ITimeEntryRepository      _repository;
    private readonly IConnectionFactory        _connectionFactory;
    private readonly ILookupCache              _lookupCache;
    private readonly ITimeApprovalNotifier     _notifier;
    private readonly ITemporalContext          _temporal;
    private readonly ILogger<TimeEntryService> _logger;

    public TimeEntryService(
        ITimeEntryRepository       repository,
        IConnectionFactory         connectionFactory,
        ILookupCache               lookupCache,
        ITimeApprovalNotifier      notifier,
        ITemporalContext           temporal,
        ILogger<TimeEntryService>  logger)
    {
        _repository        = repository;
        _connectionFactory = connectionFactory;
        _lookupCache       = lookupCache;
        _notifier          = notifier;
        _temporal          = temporal;
        _logger            = logger;
    }

    public async Task<Guid> SubmitTimeEntryAsync(SubmitTimeEntryCommand command)
    {
        var flsaStatus = await _repository.GetFlsaStatusAsync(command.EmploymentId);
        if (flsaStatus is null)
            throw new DomainException($"Employment {command.EmploymentId} not found.")
                { ExceptionCode = "EXC-TIM-001" };

        if (command.EntryMethod == "SELF_SERVICE"
            && command.SubmittedBy != command.EmploymentId)
            throw new AuthorizationException(
                "Employees may only submit time for their own employment.");

        var periodStatus = await _repository.GetPeriodStatusAsync(command.PayrollPeriodId);
        if (periodStatus is null)
            throw new DomainException("Payroll period not found.");
        // CLOSED = period closed; LOCKED = payroll released (ADR-027 D8 correction window) — neither
        // accepts routine time entry. A correction re-pays from existing approved hours; it does not
        // re-open the period for general entry.
        if (string.Equals(periodStatus, "CLOSED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(periodStatus, "LOCKED", StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Cannot submit time for a closed or locked (already-released) payroll period.")
                { ExceptionCode = "EXC-TIM-004" };

        if (string.Equals(flsaStatus, "EXEMPT", StringComparison.OrdinalIgnoreCase)
            && await _repository.IsCategoryWorkedTimeAsync(command.TimeCategory))
            throw new DomainException(
                $"EXEMPT employee: time category '{command.TimeCategory}' tracks worked hours " +
                "and cannot be submitted for FLSA-exempt employees.")
                { ExceptionCode = "EXC-TIM-005" };

        // Phase 12.7b — a trusted source (e.g. IMPORT into an auto-approve legal entity) lands the
        // entry APPROVED on submit, skipping manual review. Self-service / manual stay SUBMITTED.
        var initialStatus    = command.AutoApprove ? "APPROVED" : "SUBMITTED";
        var initialStatusId  = _lookupCache.GetId(TimeAttendanceLookupTables.TimeEntryStatus, initialStatus);
        var timeCategoryId   = _lookupCache.GetId(TimeAttendanceLookupTables.TimeCategory, command.TimeCategory);
        var entryMethodId    = _lookupCache.GetId(TimeAttendanceLookupTables.EntryMethod, command.EntryMethod);

        var entry = TimeEntry.Create(command, initialStatusId, timeCategoryId, entryMethodId, _temporal.GetOperativeNow());

        Guid entryId;
        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            entryId = await _repository.InsertAsync(entry, uow);
            uow.Commit();
        }
        catch
        {
            uow.Rollback();
            throw;
        }

        // No approval is pending for an auto-approved (already APPROVED) entry — skip the notice.
        if (!command.AutoApprove)
            await _notifier.NotifyTimeApprovalAsync(entryId, command.EmploymentId);

        // ADR-023: submission records the real event and nothing else. The regular/overtime split
        // is no longer materialized here (no shrink-original + insert-punchless-OVERTIME-row); it is
        // derived for display and computed for pay from the shared Core OvertimeSplitCalculator.
        return entryId;
    }

    public async Task ApproveTimeEntryAsync(ApproveTimeEntryCommand command)
    {
        var entry = await _repository.GetByIdAsync(command.TimeEntryId)
            ?? throw new NotFoundException(nameof(TimeEntry), command.TimeEntryId);

        if (entry.Status != TimeEntryStatus.Submitted
         && entry.Status != TimeEntryStatus.Corrected)
            throw new InvalidStateTransitionException(
                entry.Status.ToString(), TimeEntryStatus.Approved.ToString());

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await _repository.UpdateStatusAsync(command.TimeEntryId, "APPROVED", command.ApprovedBy, uow);
            uow.Commit();
        }
        catch
        {
            uow.Rollback();
            throw;
        }
    }

    public async Task<int> ApproveTimeEntriesAsync(IReadOnlyList<Guid> timeEntryIds, Guid approvedBy)
    {
        if (timeEntryIds.Count == 0) return 0;
        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            var count = await _repository.ApproveEntriesAsync(timeEntryIds, approvedBy, uow);
            uow.Commit();
            return count;
        }
        catch
        {
            uow.Rollback();
            throw;
        }
    }

    public Task<bool> GetAutoApproveImportedTimeAsync(Guid legalEntityId)
        => _repository.GetAutoApproveImportedTimeAsync(legalEntityId);

    public async Task RejectTimeEntryAsync(RejectTimeEntryCommand command)
    {
        var entry = await _repository.GetByIdAsync(command.TimeEntryId)
            ?? throw new NotFoundException(nameof(TimeEntry), command.TimeEntryId);

        if (entry.Status != TimeEntryStatus.Submitted
         && entry.Status != TimeEntryStatus.Corrected)
            throw new InvalidStateTransitionException(
                entry.Status.ToString(), TimeEntryStatus.Rejected.ToString());

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await _repository.UpdateStatusWithReasonAsync(
                command.TimeEntryId, "REJECTED", command.RejectedBy, command.Reason, uow);
            uow.Commit();
        }
        catch
        {
            uow.Rollback();
            throw;
        }
    }

    public async Task VoidTimeEntryAsync(Guid timeEntryId, Guid voidedBy, string reason)
    {
        var entry = await _repository.GetByIdAsync(timeEntryId)
            ?? throw new NotFoundException(nameof(TimeEntry), timeEntryId);

        if (entry.Status == TimeEntryStatus.Locked || entry.Status == TimeEntryStatus.Consumed)
            throw new DomainException("Locked or consumed entries cannot be voided.");

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await _repository.UpdateStatusWithReasonAsync(timeEntryId, "VOID", voidedBy, reason, uow);
            uow.Commit();
        }
        catch
        {
            uow.Rollback();
            throw;
        }
    }

    public async Task<Guid> CorrectTimeEntryAsync(CorrectTimeEntryCommand command)
    {
        var original = await _repository.GetByIdAsync(command.OriginalTimeEntryId)
            ?? throw new NotFoundException(nameof(TimeEntry), command.OriginalTimeEntryId);

        if (original.Status != TimeEntryStatus.Locked)
            throw new DomainException(
                "Only Locked entries can be corrected through the correction workflow.");

        var submittedStatusId = _lookupCache.GetId(TimeAttendanceLookupTables.TimeEntryStatus, "SUBMITTED");
        var timeCategoryId    = _lookupCache.GetId(TimeAttendanceLookupTables.TimeCategory, command.TimeCategory);

        var correction = TimeEntry.CreateCorrection(original, command, submittedStatusId, timeCategoryId, _temporal.GetOperativeNow());

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            var correctionId = await _repository.InsertAsync(correction, uow);
            uow.Commit();

            if (command.RetroactiveFlag)
                await _notifier.NotifyRetroCalculationReviewAsync(
                    correctionId, original.EmploymentId, original.PayrollPeriodId);

            return correctionId;
        }
        catch
        {
            uow.Rollback();
            throw;
        }
    }

    public Task<IEnumerable<TimeEntry>> GetPeriodEntriesAsync(Guid employmentId, Guid payrollPeriodId)
        => _repository.GetByEmploymentAndPeriodAsync(employmentId, payrollPeriodId);

}
