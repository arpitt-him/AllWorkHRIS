using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Benefits.Domain.Codes;

namespace AllWorkHRIS.Module.Benefits.Repositories;

public interface IDeductionEmployerMatchRepository
{
    // Pipeline use: returns the most-specific active match rule for a deduction.
    // Checks group-specific rule first, falls back to the universal rule (employee_group_id IS NULL).
    Task<DeductionEmployerMatch?>             GetActiveByDeductionIdAsync(
        Guid deductionId, DateOnly asOf, Guid? employeeGroupId = null, CancellationToken ct = default);

    // Admin UI: list all match rules (including expired) for a deduction.
    Task<IEnumerable<DeductionEmployerMatch>> GetAllByDeductionIdAsync(Guid deductionId, CancellationToken ct = default);

    Task<Guid>                                InsertAsync(DeductionEmployerMatch match, IUnitOfWork uow);
    Task                                      UpdateAsync(DeductionEmployerMatch match, IUnitOfWork uow);

    // Convenience overloads — wrap in their own UoW.
    Task<Guid>                                InsertAsync(DeductionEmployerMatch match);
    Task                                      UpdateAsync(DeductionEmployerMatch match);
}
