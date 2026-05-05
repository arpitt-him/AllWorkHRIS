using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Benefits.Domain.Codes;

namespace AllWorkHRIS.Module.Benefits.Repositories;

public interface IDeductionRepository
{
    Task<Deduction?>             GetByIdAsync(Guid deductionId, CancellationToken ct = default);
    Task<Deduction?>             GetByCodeAsync(string code, CancellationToken ct = default);
    Task<IEnumerable<Deduction>> GetActiveCodesAsync(CancellationToken ct = default);
    Task<IEnumerable<Deduction>> GetAllCodesAsync(CancellationToken ct = default);
    Task<Guid>                   InsertAsync(Deduction deduction, IUnitOfWork uow);
    Task                         UpdateAsync(Deduction deduction, IUnitOfWork uow);
    Task<Guid>                   InsertAsync(Deduction deduction);
    Task                         UpdateAsync(Deduction deduction);
}
