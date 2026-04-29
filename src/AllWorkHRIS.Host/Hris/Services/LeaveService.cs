using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Events;
using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Core.Temporal;
using AllWorkHRIS.Host.Hris.Commands;
using AllWorkHRIS.Host.Hris.Domain;
using AllWorkHRIS.Host.Hris.Repositories;

namespace AllWorkHRIS.Host.Hris.Services;

// ============================================================
// ADDITIONAL EXCEPTIONS
// ============================================================

public sealed class InsufficientLeaveBalanceException : Exception
{
    public string  LeaveType        { get; }
    public decimal RequestedDays    { get; }
    public decimal AvailableBalance { get; }

    public InsufficientLeaveBalanceException(
        string leaveType, decimal requestedDays, decimal availableBalance)
        : base($"Insufficient {leaveType} balance. Requested {requestedDays} days; " +
               $"available {availableBalance} days.")
    {
        LeaveType        = leaveType;
        RequestedDays    = requestedDays;
        AvailableBalance = availableBalance;
    }
}

public sealed class InvalidStateTransitionException : Exception
{
    public InvalidStateTransitionException(string from, string to)
        : base($"Cannot transition leave request from {from} to {to}.") { }
}

public sealed class NotFoundException : Exception
{
    public NotFoundException(string entityType, Guid id)
        : base($"{entityType} {id} not found.") { }
}

// ============================================================
// LEAVE SERVICE
// ============================================================

public interface ILeaveService
{
    Task<Guid>                      SubmitLeaveRequestAsync(SubmitLeaveRequestCommand command);
    Task                            ApproveLeaveRequestAsync(ApproveLeaveRequestCommand command);
    Task                            DenyLeaveRequestAsync(DenyLeaveRequestCommand command);
    Task                            CancelLeaveRequestAsync(Guid leaveRequestId, Guid cancelledBy);
    Task                            ReturnFromLeaveAsync(ReturnFromLeaveCommand command);
    Task<IEnumerable<LeaveBalance>> GetBalancesAsync(Guid employmentId);
    Task<IEnumerable<LeaveRequest>> GetHistoryAsync(Guid employmentId);
}

public sealed class LeaveService : ILeaveService
{
    private readonly IConnectionFactory          _connectionFactory;
    private readonly ILeaveRequestRepository     _leaveRequestRepository;
    private readonly ILeaveBalanceRepository     _leaveBalanceRepository;
    private readonly ILeaveTypeConfigRepository  _leaveTypeConfigRepository;
    private readonly IWorkQueueService           _workQueueService;
    private readonly IEventPublisher             _eventPublisher;
    private readonly ITemporalContext            _temporalContext;
    private readonly ILookupCache                _lookupCache;

    private readonly int _requestedStatusId;
    private readonly int _approvedStatusId;
    private readonly int _inProgressStatusId;
    private readonly int _completedStatusId;
    private readonly int _deniedStatusId;
    private readonly int _cancelledStatusId;

    public LeaveService(
        IConnectionFactory         connectionFactory,
        ILeaveRequestRepository    leaveRequestRepository,
        ILeaveBalanceRepository    leaveBalanceRepository,
        ILeaveTypeConfigRepository leaveTypeConfigRepository,
        IWorkQueueService          workQueueService,
        IEventPublisher            eventPublisher,
        ITemporalContext           temporalContext,
        ILookupCache               lookupCache)
    {
        _connectionFactory         = connectionFactory;
        _leaveRequestRepository    = leaveRequestRepository;
        _leaveBalanceRepository    = leaveBalanceRepository;
        _leaveTypeConfigRepository = leaveTypeConfigRepository;
        _workQueueService          = workQueueService;
        _eventPublisher            = eventPublisher;
        _temporalContext           = temporalContext;
        _lookupCache               = lookupCache;

        _requestedStatusId  = lookupCache.GetId(LookupTables.LeaveStatus, "REQUESTED");
        _approvedStatusId   = lookupCache.GetId(LookupTables.LeaveStatus, "APPROVED");
        _inProgressStatusId = lookupCache.GetId(LookupTables.LeaveStatus, "IN_PROGRESS");
        _completedStatusId  = lookupCache.GetId(LookupTables.LeaveStatus, "COMPLETED");
        _deniedStatusId     = lookupCache.GetId(LookupTables.LeaveStatus, "DENIED");
        _cancelledStatusId  = lookupCache.GetId(LookupTables.LeaveStatus, "CANCELLED");
    }

    public async Task<Guid> SubmitLeaveRequestAsync(SubmitLeaveRequestCommand command)
    {
        if (command.LeaveEndDate < command.LeaveStartDate)
            throw new ValidationException("Leave end date cannot be before start date.");

        var leaveTypeInfo = await _leaveTypeConfigRepository.GetByCodeAsync(command.LeaveType)
            ?? throw new ValidationException($"Unknown leave type: {command.LeaveType}");

        var overlapping = await _leaveRequestRepository.GetOverlappingAsync(
            command.EmploymentId, command.LeaveStartDate, command.LeaveEndDate,
            ["REQUESTED", "APPROVED", "IN_PROGRESS"]);

        if (overlapping.Any())
            throw new DomainException("An overlapping leave request already exists.");

        var requestedDays = LeaveService.CalculateWorkingDays(
            command.LeaveStartDate, command.LeaveEndDate);

        if (leaveTypeInfo.IsAccrued)
        {
            var balance = await _leaveBalanceRepository.GetByEmploymentAndTypeAsync(
                command.EmploymentId, leaveTypeInfo.Id);

            if (balance is null || balance.AvailableBalance < requestedDays)
                throw new InsufficientLeaveBalanceException(
                    command.LeaveType, requestedDays,
                    balance?.AvailableBalance ?? 0);
        }

        var payrollImpactTypeId = _lookupCache.GetId(
            LookupTables.PayrollImpactType, leaveTypeInfo.PayrollImpactCode);

        var now = DateTimeOffset.UtcNow;
        var request = new LeaveRequest
        {
            LeaveRequestId      = Guid.NewGuid(),
            EmploymentId        = command.EmploymentId,
            LeaveTypeId         = leaveTypeInfo.Id,
            RequestDate         = DateOnly.FromDateTime(_temporalContext.GetOperativeDate()),
            LeaveStartDate      = command.LeaveStartDate,
            LeaveEndDate        = command.LeaveEndDate,
            LeaveStatusId       = _requestedStatusId,
            LeaveReasonCode     = command.LeaveReasonCode,
            PayrollImpactTypeId = payrollImpactTypeId,
            Notes               = command.Notes,
            CreatedBy           = command.SubmittedBy,
            CreationTimestamp   = now,
            LastUpdatedBy       = command.SubmittedBy,
            LastUpdateTimestamp = now
        };

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            var leaveRequestId = await _leaveRequestRepository.InsertAsync(request, uow);
            uow.Commit();

            await _workQueueService.CreateLeaveApprovalTaskAsync(
                leaveRequestId, command.EmploymentId);

            return leaveRequestId;
        }
        catch
        {
            uow.Rollback();
            throw;
        }
    }

    public async Task ApproveLeaveRequestAsync(ApproveLeaveRequestCommand command)
    {
        var request = await _leaveRequestRepository.GetByIdAsync(command.LeaveRequestId)
            ?? throw new NotFoundException(nameof(LeaveRequest), command.LeaveRequestId);

        if (request.LeaveStatusId != _requestedStatusId)
            throw new InvalidStateTransitionException("REQUESTED", "APPROVED");

        var leaveTypeInfo = await _leaveTypeConfigRepository.GetByCodeAsync(
            _lookupCache.GetCode(LookupTables.LeaveType, request.LeaveTypeId))
            ?? throw new DomainException("Leave type configuration not found.");

        var requestedDays = CalculateWorkingDays(request.LeaveStartDate, request.LeaveEndDate);
        var now = DateTimeOffset.UtcNow;

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await _leaveRequestRepository.UpdateStatusAsync(
                command.LeaveRequestId, _approvedStatusId, command.ApprovedBy, uow);

            await _leaveRequestRepository.SetApprovalAsync(
                command.LeaveRequestId, command.ApprovedBy, now, uow);

            if (leaveTypeInfo.IsAccrued)
            {
                await _leaveBalanceRepository.DeductBalanceAsync(
                    request.EmploymentId, request.LeaveTypeId, requestedDays, uow);
            }

            uow.Commit();

            await _eventPublisher.PublishAsync(new LeaveApprovedPayload
            {
                LeaveRequestId    = command.LeaveRequestId,
                EmploymentId      = request.EmploymentId,
                TenantId          = Guid.Empty,
                LeaveType         = _lookupCache.GetCode(LookupTables.LeaveType, request.LeaveTypeId),
                LeaveStartDate    = request.LeaveStartDate,
                LeaveEndDate      = request.LeaveEndDate,
                PayrollImpactType = leaveTypeInfo.PayrollImpactCode,
                ApprovedBy        = command.ApprovedBy,
                EventTimestamp    = now
            });

            await _workQueueService.ResolveByReferenceAsync(
                command.LeaveRequestId, command.ApprovedBy);
        }
        catch
        {
            uow.Rollback();
            throw;
        }
    }

    public async Task DenyLeaveRequestAsync(DenyLeaveRequestCommand command)
    {
        var request = await _leaveRequestRepository.GetByIdAsync(command.LeaveRequestId)
            ?? throw new NotFoundException(nameof(LeaveRequest), command.LeaveRequestId);

        if (request.LeaveStatusId != _requestedStatusId)
            throw new InvalidStateTransitionException("REQUESTED", "DENIED");

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await _leaveRequestRepository.UpdateStatusAsync(
                command.LeaveRequestId, _deniedStatusId, command.DeniedBy, uow);
            uow.Commit();

            await _workQueueService.ResolveByReferenceAsync(
                command.LeaveRequestId, command.DeniedBy);
        }
        catch
        {
            uow.Rollback();
            throw;
        }
    }

    public async Task CancelLeaveRequestAsync(Guid leaveRequestId, Guid cancelledBy)
    {
        var request = await _leaveRequestRepository.GetByIdAsync(leaveRequestId)
            ?? throw new NotFoundException(nameof(LeaveRequest), leaveRequestId);

        if (request.LeaveStatusId == _inProgressStatusId)
            throw new InvalidStateTransitionException("IN_PROGRESS", "CANCELLED");

        if (request.LeaveStatusId == _completedStatusId ||
            request.LeaveStatusId == _deniedStatusId    ||
            request.LeaveStatusId == _cancelledStatusId)
            throw new InvalidStateTransitionException(
                _lookupCache.GetCode(LookupTables.LeaveStatus, request.LeaveStatusId), "CANCELLED");

        var leaveTypeInfo = await _leaveTypeConfigRepository.GetByCodeAsync(
            _lookupCache.GetCode(LookupTables.LeaveType, request.LeaveTypeId));

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await _leaveRequestRepository.UpdateStatusAsync(
                leaveRequestId, _cancelledStatusId, cancelledBy, uow);

            if (leaveTypeInfo?.IsAccrued == true && request.LeaveStatusId == _approvedStatusId)
            {
                var requestedDays = CalculateWorkingDays(request.LeaveStartDate, request.LeaveEndDate);
                await _leaveBalanceRepository.RestoreBalanceAsync(
                    request.EmploymentId, request.LeaveTypeId, requestedDays, uow);
            }

            uow.Commit();
            await _workQueueService.ResolveByReferenceAsync(leaveRequestId, cancelledBy);
        }
        catch
        {
            uow.Rollback();
            throw;
        }
    }

    public async Task ReturnFromLeaveAsync(ReturnFromLeaveCommand command)
    {
        var activeLeave = (await _leaveRequestRepository.GetByEmploymentIdAsync(command.EmploymentId))
            .FirstOrDefault(r => r.LeaveStatusId == _inProgressStatusId)
            ?? throw new DomainException(
                $"No active leave found for employment {command.EmploymentId}.");

        using var uow = new UnitOfWork(_connectionFactory);
        try
        {
            await _leaveRequestRepository.SetReturnDateAsync(
                activeLeave.LeaveRequestId, command.ReturnDate, uow);
            await _leaveRequestRepository.UpdateStatusAsync(
                activeLeave.LeaveRequestId, _completedStatusId, command.InitiatedBy, uow);
            uow.Commit();

            await _eventPublisher.PublishAsync(new ReturnToWorkPayload
            {
                LeaveRequestId = activeLeave.LeaveRequestId,
                EmploymentId   = command.EmploymentId,
                TenantId       = Guid.Empty,
                ReturnDate     = command.ReturnDate,
                InitiatedBy    = command.InitiatedBy,
                EventTimestamp = DateTimeOffset.UtcNow
            });
        }
        catch
        {
            uow.Rollback();
            throw;
        }
    }

    public Task<IEnumerable<LeaveBalance>> GetBalancesAsync(Guid employmentId)
        => _leaveBalanceRepository.GetAllByEmploymentIdAsync(employmentId);

    public Task<IEnumerable<LeaveRequest>> GetHistoryAsync(Guid employmentId)
        => _leaveRequestRepository.GetByEmploymentIdAsync(employmentId);

    public static decimal CalculateWorkingDays(DateOnly start, DateOnly end,
        IEnumerable<DateOnly>? companyHolidays = null)
    {
        decimal days = 0;
        var current  = start;
        var holidays = companyHolidays?.ToHashSet() ?? [];

        while (current <= end)
        {
            if (current.DayOfWeek != DayOfWeek.Saturday
             && current.DayOfWeek != DayOfWeek.Sunday
             && !holidays.Contains(current))
                days++;

            current = current.AddDays(1);
        }

        return days;
    }
}
