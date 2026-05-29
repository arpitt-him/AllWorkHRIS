using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Reporting.Domain;

namespace AllWorkHRIS.Module.Reporting.Repositories;

/// <summary>
/// CRUD over <c>report_execution_history</c>. Inserts and status transitions
/// run inside a caller-owned <see cref="IUnitOfWork"/>; reads use a short-lived
/// connection. All timestamps come from the caller (via
/// <see cref="AllWorkHRIS.Core.ITemporalContext"/>) — the repository never
/// touches the clock.
/// </summary>
public interface IReportHistoryRepository
{
    Task<Guid> InsertAsync(ReportExecutionHistory execution, IUnitOfWork uow);

    Task UpdateCompletedAsync(Guid executionId, int rowCount, string? storageReference, DateTime completedAt, IUnitOfWork uow);

    Task UpdateFailedAsync(Guid executionId, string errorMessage, DateTime completedAt, IUnitOfWork uow);

    Task UpdateAsyncCompletedAsync(Guid executionId, Guid jobId, int rowCount, string? storageReference, DateTime completedAt, IUnitOfWork uow);

    Task<ReportExecutionHistory?> GetByIdAsync(Guid executionId);

    Task<IEnumerable<ReportExecutionHistory>> GetRecentByUserAsync(Guid requestedBy, int count = 20);

    Task<IEnumerable<ReportExecutionHistory>> GetRecentByUserAndReportAsync(Guid requestedBy, string reportId, int count = 5);

    Task<IEnumerable<ReportExecutionHistory>> GetByReportAndDateRangeAsync(string reportId, DateOnly from, DateOnly to);

    Task<IEnumerable<ReportExecutionHistory>> GetByUserAndDateRangeAsync(Guid requestedBy, DateOnly from, DateOnly to);
}
