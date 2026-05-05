using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Domain;
using AllWorkHRIS.Module.Benefits.Domain.Elections;
using AllWorkHRIS.Module.Benefits.Queries;

namespace AllWorkHRIS.Module.Benefits.Repositories;

public interface IBenefitElectionRepository
{
    Task<BenefitDeductionElection?>             GetByIdAsync(Guid electionId, CancellationToken ct = default);
    Task<IEnumerable<BenefitDeductionElection>> GetByEmploymentIdAsync(Guid employmentId, CancellationToken ct = default);
    Task<IEnumerable<BenefitDeductionElection>> GetActiveByEmploymentIdAsync(Guid employmentId, DateOnly asOf, CancellationToken ct = default);

    // Returns all non-SUPERSEDED elections whose date range overlaps the pay period.
    // Used by the payroll pipeline to retrieve elections that are partially in-period
    // (mid-period hire, termination, or coverage change).
    Task<IEnumerable<BenefitDeductionElection>> GetElectionsOverlappingPeriodAsync(
        Guid employmentId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default);

    // Bulk variant of GetElectionsOverlappingPeriodAsync for payroll batch runs.
    Task<IEnumerable<BenefitDeductionElection>> GetNonSupersededByEmploymentIdsAsync(
        IReadOnlyList<Guid> employmentIds, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default);

    Task<BenefitDeductionElection?>             GetActiveByCodeAsync(Guid employmentId, string deductionCode, DateOnly asOf, CancellationToken ct = default);
    Task<IEnumerable<BenefitDeductionElection>> GetSuspendedByEmploymentIdAsync(Guid employmentId, CancellationToken ct = default);

    // Returns true when a non-SUPERSEDED/TERMINATED election for the same deduction
    // overlaps [start, end]. Used by the SERIALIZABLE overlap guard in CreateElectionAsync.
    Task<bool>                                  HasOverlapAsync(Guid employmentId, Guid deductionId, DateOnly start, DateOnly? end, CancellationToken ct = default);

    // Same check executed within an existing transaction (for SERIALIZABLE scope).
    Task<bool>                                  HasOverlapAsync(Guid employmentId, Guid deductionId, DateOnly start, DateOnly? end, IUnitOfWork uow, CancellationToken ct = default);

    // Returns all non-SUPERSEDED/TERMINATED elections for the given deduction that overlap [start, end].
    // Used inside a SERIALIZABLE scope by ImportElectionAsync to supersede prior elections before insert.
    Task<IEnumerable<BenefitDeductionElection>> GetOverlappingByDeductionAsync(Guid employmentId, Guid deductionId, DateOnly start, DateOnly? end, IUnitOfWork uow, CancellationToken ct = default);

    Task<Guid>                                  InsertAsync(BenefitDeductionElection election, IUnitOfWork uow);
    Task                                        UpdateStatusAsync(Guid electionId, string status, IUnitOfWork uow);
    Task                                        UpdateStatusWithEventAsync(Guid electionId, string status, Guid sourceEventId, IUnitOfWork uow);
    Task                                        TrimEndDateAsync(Guid electionId, DateOnly newEndDate, IUnitOfWork uow);
    Task                                        TerminateAsync(Guid electionId, DateOnly effectiveEndDate, IUnitOfWork uow);
    Task                                        TerminateWithEventAsync(Guid electionId, DateOnly effectiveEndDate, Guid sourceEventId, IUnitOfWork uow);
    Task<PagedResult<ElectionListItem>>         GetPagedListAsync(ElectionListQuery query, CancellationToken ct = default);

    // Promotes all PENDING elections whose effective_start_date has passed to ACTIVE.
    // Returns the number of elections activated.
    Task<int>                                   ActivatePendingAsync(DateOnly asOf, CancellationToken ct = default);
}
