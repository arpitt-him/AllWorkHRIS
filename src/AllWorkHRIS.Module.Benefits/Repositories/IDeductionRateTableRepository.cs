using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Benefits.Domain.Codes;

namespace AllWorkHRIS.Module.Benefits.Repositories;

public interface IDeductionRateTableRepository
{
    // Pipeline use: get the single active table for a deduction at calculation time.
    Task<DeductionRateTable?>             GetActiveByDeductionIdAsync(Guid deductionId, DateOnly asOf, CancellationToken ct = default);

    // Admin UI: list all tables (including expired) for a deduction.
    Task<IEnumerable<DeductionRateTable>> GetAllByDeductionIdAsync(Guid deductionId, CancellationToken ct = default);

    // Returns all rate entries for a given table (used by both pipeline and admin UI).
    Task<IEnumerable<DeductionRateEntry>> GetEntriesAsync(Guid rateTableId, CancellationToken ct = default);

    Task<Guid>                            InsertTableAsync(DeductionRateTable table, IUnitOfWork uow);
    Task<Guid>                            InsertEntryAsync(DeductionRateEntry entry, IUnitOfWork uow);

    // Replaces all entries for a table atomically (used by admin save).
    Task                                  ReplaceEntriesAsync(Guid rateTableId, IEnumerable<DeductionRateEntry> entries, IUnitOfWork uow);

    // Closes any open-ended table(s) for a deduction by setting effective_to = closingDate.
    Task                                  CloseOpenTablesAsync(Guid deductionId, DateOnly closingDate, IUnitOfWork uow);

    // Convenience overload — wraps InsertTableAsync in its own UoW.
    Task<Guid>                            InsertTableAsync(DeductionRateTable table);
}
