using AllWorkHRIS.Module.Payroll.Domain.ResultSet;

namespace AllWorkHRIS.Module.Payroll.Repositories;

public interface IPayrollRunResultSetRepository
{
    Task<PayrollRunResultSet?> GetByIdAsync(Guid resultSetId);
    Task<IReadOnlyList<PayrollRunResultSet>> GetByRunIdAsync(Guid runId);
    Task<Guid> InsertAsync(PayrollRunResultSet resultSet);
    Task UpdateStatusAsync(Guid resultSetId, int statusId);
    Task SetTimestampsAsync(Guid resultSetId, DateTimeOffset? startTimestamp, DateTimeOffset? endTimestamp);
}
