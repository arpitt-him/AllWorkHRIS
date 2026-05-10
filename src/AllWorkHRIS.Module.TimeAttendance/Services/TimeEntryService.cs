using AllWorkHRIS.Core.Composition;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Module.TimeAttendance.Commands;
using AllWorkHRIS.Module.TimeAttendance.Domain;
using AllWorkHRIS.Module.TimeAttendance.Repositories;
using Microsoft.Extensions.Logging;

namespace AllWorkHRIS.Module.TimeAttendance.Services;

public sealed class TimeEntryService : ITimeEntryService
{
    private readonly ITimeEntryRepository      _repository;
    private readonly IOvertimeDetectionService _overtimeService;
    private readonly IWorkScheduleRepository   _workSchedules;
    private readonly IConnectionFactory        _connectionFactory;
    private readonly ILookupCache              _lookupCache;
    private readonly ITimeApprovalNotifier     _notifier;
    private readonly ILogger<TimeEntryService> _logger;

    public TimeEntryService(
        ITimeEntryRepository       repository,
        IOvertimeDetectionService  overtimeService,
        IWorkScheduleRepository    workSchedules,
        IConnectionFactory         connectionFactory,
        ILookupCache               lookupCache,
        ITimeApprovalNotifier      notifier,
        ILogger<TimeEntryService>  logger)
    {
        _repository        = repository;
        _overtimeService   = overtimeService;
        _workSchedules     = workSchedules;
        _connectionFactory = connectionFactory;
        _lookupCache       = lookupCache;
        _notifier          = notifier;
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
        if (string.Equals(periodStatus, "CLOSED", StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Cannot submit time for a closed payroll period.")
                { ExceptionCode = "EXC-TIM-004" };

        if (string.Equals(flsaStatus, "EXEMPT", StringComparison.OrdinalIgnoreCase)
            && await _repository.IsCategoryWorkedTimeAsync(command.TimeCategory))
            throw new DomainException(
                $"EXEMPT employee: time category '{command.TimeCategory}' tracks worked hours " +
                "and cannot be submitted for FLSA-exempt employees.")
                { ExceptionCode = "EXC-TIM-005" };

        var submittedStatusId = _lookupCache.GetId(TimeAttendanceLookupTables.TimeEntryStatus, "SUBMITTED");
        var timeCategoryId    = _lookupCache.GetId(TimeAttendanceLookupTables.TimeCategory, command.TimeCategory);
        var entryMethodId     = _lookupCache.GetId(TimeAttendanceLookupTables.EntryMethod, command.EntryMethod);

        var entry = TimeEntry.Create(command, submittedStatusId, timeCategoryId, entryMethodId);

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            var entryId = await _repository.InsertAsync(entry, uow);
            uow.Commit();

            await _notifier.NotifyTimeApprovalAsync(entryId, command.EmploymentId);
            return entryId;
        }
        catch
        {
            uow.Rollback();
            throw;
        }
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

        // Overtime detection runs after approval in its own transaction
        var anchor    = await _workSchedules.ResolveWorkweekAnchorAsync(entry.PayrollPeriodId, entry.EmploymentId);
        var diff      = ((int)entry.WorkDate.DayOfWeek - anchor + 7) % 7;
        var weekStart = entry.WorkDate.AddDays(-diff);
        using var otUow = new UnitOfWork(_connectionFactory);
        try
        {
            await _overtimeService.DetectAndReclassifyAsync(entry.EmploymentId, weekStart, otUow);
            otUow.Commit();
        }
        catch (Exception ex)
        {
            otUow.Rollback();
            _logger.LogWarning(ex,
                "Overtime detection failed after approving entry {TimeEntryId} — approval stands",
                command.TimeEntryId);
        }
    }

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

        var correction = TimeEntry.CreateCorrection(original, command, submittedStatusId, timeCategoryId);

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
