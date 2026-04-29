using Dapper;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Host.Hris.Domain;

namespace AllWorkHRIS.Host.Hris.Repositories;

// ============================================================
// LEAVE REQUEST
// ============================================================

public interface ILeaveRequestRepository
{
    Task<LeaveRequest?>             GetByIdAsync(Guid leaveRequestId);
    Task<IEnumerable<LeaveRequest>> GetByEmploymentIdAsync(Guid employmentId);
    Task<IEnumerable<LeaveRequest>> GetOverlappingAsync(Guid employmentId,
                                        DateOnly startDate, DateOnly endDate,
                                        IEnumerable<string> activeStatusCodes);
    Task<IEnumerable<LeaveRequest>> GetByStatusAsync(string statusCode);
    Task<Guid>                      InsertAsync(LeaveRequest request, IUnitOfWork uow);
    Task                            UpdateStatusAsync(Guid leaveRequestId, int statusId,
                                        Guid actorId, IUnitOfWork uow);
    Task                            SetReturnDateAsync(Guid leaveRequestId,
                                        DateOnly returnDate, IUnitOfWork uow);
    Task                            SetApprovalAsync(Guid leaveRequestId, Guid approvedBy,
                                        DateTimeOffset timestamp, IUnitOfWork uow);
}

public sealed class LeaveRequestRepository : ILeaveRequestRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public LeaveRequestRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<LeaveRequest?> GetByIdAsync(Guid leaveRequestId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<LeaveRequest>(
            "SELECT * FROM leave_request WHERE leave_request_id = @Id",
            new { Id = leaveRequestId });
    }

    public async Task<IEnumerable<LeaveRequest>> GetByEmploymentIdAsync(Guid employmentId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<LeaveRequest>(
            "SELECT * FROM leave_request WHERE employment_id = @Id ORDER BY leave_start_date DESC",
            new { Id = employmentId });
    }

    public async Task<IEnumerable<LeaveRequest>> GetOverlappingAsync(Guid employmentId,
        DateOnly startDate, DateOnly endDate, IEnumerable<string> activeStatusCodes)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<LeaveRequest>(
            @"SELECT lr.* FROM leave_request lr
              JOIN lkp_leave_status ls ON lr.leave_status_id = ls.id
              WHERE lr.employment_id = @EmploymentId
                AND ls.code = ANY(@Codes)
                AND lr.leave_start_date <= @EndDate
                AND lr.leave_end_date   >= @StartDate",
            new
            {
                EmploymentId = employmentId,
                StartDate    = startDate.ToDateTime(TimeOnly.MinValue),
                EndDate      = endDate.ToDateTime(TimeOnly.MinValue),
                Codes        = activeStatusCodes.ToArray()
            });
    }

    public async Task<IEnumerable<LeaveRequest>> GetByStatusAsync(string statusCode)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<LeaveRequest>(
            @"SELECT lr.* FROM leave_request lr
              JOIN lkp_leave_status ls ON lr.leave_status_id = ls.id
              WHERE ls.code = @Code",
            new { Code = statusCode });
    }

    public async Task<Guid> InsertAsync(LeaveRequest request, IUnitOfWork uow)
    {
        const string sql =
            @"INSERT INTO leave_request (
                leave_request_id, employment_id, leave_type_id, request_date,
                leave_start_date, leave_end_date, leave_status_id, leave_reason_code,
                payroll_impact_type_id, leave_balance_impact, fmla_eligible_flag,
                notes, created_by, creation_timestamp, last_updated_by, last_update_timestamp)
              VALUES (
                @LeaveRequestId, @EmploymentId, @LeaveTypeId,
                @RequestDate, @LeaveStartDate, @LeaveEndDate,
                @LeaveStatusId, @LeaveReasonCode, @PayrollImpactTypeId,
                @LeaveBalanceImpact, @FmlaEligibleFlag,
                @Notes, @CreatedBy, @CreationTimestamp, @LastUpdatedBy, @LastUpdateTimestamp)
              RETURNING leave_request_id";

        return await uow.Connection.ExecuteScalarAsync<Guid>(sql,
            new
            {
                request.LeaveRequestId,
                request.EmploymentId,
                request.LeaveTypeId,
                RequestDate            = request.RequestDate.ToDateTime(TimeOnly.MinValue),
                LeaveStartDate         = request.LeaveStartDate.ToDateTime(TimeOnly.MinValue),
                LeaveEndDate           = request.LeaveEndDate.ToDateTime(TimeOnly.MinValue),
                request.LeaveStatusId,
                request.LeaveReasonCode,
                request.PayrollImpactTypeId,
                request.LeaveBalanceImpact,
                request.FmlaEligibleFlag,
                request.Notes,
                request.CreatedBy,
                CreationTimestamp      = request.CreationTimestamp,
                request.LastUpdatedBy,
                LastUpdateTimestamp    = request.LastUpdateTimestamp
            },
            uow.Transaction);
    }

    public async Task UpdateStatusAsync(Guid leaveRequestId, int statusId,
        Guid actorId, IUnitOfWork uow)
    {
        await uow.Connection.ExecuteAsync(
            @"UPDATE leave_request
              SET leave_status_id = @StatusId, last_updated_by = @ActorId,
                  last_update_timestamp = now()
              WHERE leave_request_id = @Id",
            new { Id = leaveRequestId, StatusId = statusId, ActorId = actorId },
            uow.Transaction);
    }

    public async Task SetReturnDateAsync(Guid leaveRequestId, DateOnly returnDate, IUnitOfWork uow)
    {
        await uow.Connection.ExecuteAsync(
            @"UPDATE leave_request
              SET actual_return_date = @ReturnDate, last_update_timestamp = now()
              WHERE leave_request_id = @Id",
            new { Id = leaveRequestId, ReturnDate = returnDate.ToDateTime(TimeOnly.MinValue) },
            uow.Transaction);
    }

    public async Task SetApprovalAsync(Guid leaveRequestId, Guid approvedBy,
        DateTimeOffset timestamp, IUnitOfWork uow)
    {
        await uow.Connection.ExecuteAsync(
            @"UPDATE leave_request
              SET approved_by = @ApprovedBy, approval_timestamp = @Timestamp,
                  last_update_timestamp = now()
              WHERE leave_request_id = @Id",
            new { Id = leaveRequestId, ApprovedBy = approvedBy, Timestamp = timestamp },
            uow.Transaction);
    }
}

// ============================================================
// LEAVE BALANCE
// ============================================================

public interface ILeaveBalanceRepository
{
    Task<LeaveBalance?>             GetByEmploymentAndTypeAsync(Guid employmentId, int leaveTypeId);
    Task<IEnumerable<LeaveBalance>> GetAllByEmploymentIdAsync(Guid employmentId);
    Task                            DeductBalanceAsync(Guid employmentId, int leaveTypeId,
                                        decimal days, IUnitOfWork uow);
    Task                            RestoreBalanceAsync(Guid employmentId, int leaveTypeId,
                                        decimal days, IUnitOfWork uow);
}

public sealed class LeaveBalanceRepository : ILeaveBalanceRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public LeaveBalanceRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<LeaveBalance?> GetByEmploymentAndTypeAsync(Guid employmentId, int leaveTypeId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<LeaveBalance>(
            @"SELECT * FROM leave_balance
              WHERE employment_id = @EmploymentId AND leave_type_id = @LeaveTypeId
              ORDER BY plan_year_start DESC
              LIMIT 1",
            new { EmploymentId = employmentId, LeaveTypeId = leaveTypeId });
    }

    public async Task<IEnumerable<LeaveBalance>> GetAllByEmploymentIdAsync(Guid employmentId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<LeaveBalance>(
            @"SELECT lb.*, lt.code as leave_type_code
              FROM leave_balance lb
              JOIN lkp_leave_type lt ON lb.leave_type_id = lt.id
              WHERE lb.employment_id = @EmploymentId
              ORDER BY lb.plan_year_start DESC",
            new { EmploymentId = employmentId });
    }

    public async Task DeductBalanceAsync(Guid employmentId, int leaveTypeId,
        decimal days, IUnitOfWork uow)
    {
        await uow.Connection.ExecuteAsync(
            @"UPDATE leave_balance
              SET available_balance = available_balance - @Days,
                  pending_balance   = pending_balance + @Days,
                  used_balance      = used_balance + @Days,
                  last_update_timestamp = now()
              WHERE employment_id = @EmploymentId AND leave_type_id = @LeaveTypeId
                AND plan_year_start = (
                    SELECT MAX(plan_year_start) FROM leave_balance
                    WHERE employment_id = @EmploymentId AND leave_type_id = @LeaveTypeId)",
            new { EmploymentId = employmentId, LeaveTypeId = leaveTypeId, Days = days },
            uow.Transaction);
    }

    public async Task RestoreBalanceAsync(Guid employmentId, int leaveTypeId,
        decimal days, IUnitOfWork uow)
    {
        await uow.Connection.ExecuteAsync(
            @"UPDATE leave_balance
              SET available_balance = available_balance + @Days,
                  pending_balance   = GREATEST(pending_balance - @Days, 0),
                  used_balance      = GREATEST(used_balance - @Days, 0),
                  last_update_timestamp = now()
              WHERE employment_id = @EmploymentId AND leave_type_id = @LeaveTypeId
                AND plan_year_start = (
                    SELECT MAX(plan_year_start) FROM leave_balance
                    WHERE employment_id = @EmploymentId AND leave_type_id = @LeaveTypeId)",
            new { EmploymentId = employmentId, LeaveTypeId = leaveTypeId, Days = days },
            uow.Transaction);
    }
}

// ============================================================
// LEAVE TYPE CONFIG (read-only helper — queries joined lkp tables)
// ============================================================

public interface ILeaveTypeConfigRepository
{
    Task<LeaveTypeInfo?> GetByCodeAsync(string leaveTypeCode);
}

public sealed class LeaveTypeConfigRepository : ILeaveTypeConfigRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public LeaveTypeConfigRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<LeaveTypeInfo?> GetByCodeAsync(string leaveTypeCode)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<LeaveTypeInfo>(
            @"SELECT lt.id, lt.code, lt.is_accrued,
                     pit.code as payroll_impact_code,
                     pit.pay_percentage
              FROM lkp_leave_type lt
              JOIN lkp_payroll_impact_type pit ON lt.payroll_impact_type_id = pit.id
              WHERE lt.code = @Code AND lt.is_active = true",
            new { Code = leaveTypeCode });
    }
}
