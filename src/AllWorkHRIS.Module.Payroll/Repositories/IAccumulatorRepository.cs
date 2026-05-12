using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Payroll.Domain.Accumulators;

namespace AllWorkHRIS.Module.Payroll.Repositories;

public interface IAccumulatorRepository
{
    Task<AccumulatorDefinition?> GetDefinitionByCodeAsync(string accumulatorCode, DateOnly asOf);
    Task<IReadOnlyList<AccumulatorDefinition>> GetAllActiveDefinitionsAsync(DateOnly asOf);

    Task<AccumulatorBalance?> GetBalanceAsync(Guid accumulatorDefinitionId, Guid? employmentId,
        Guid? legalEntityId, Guid periodId);

    /// <summary>
    /// Returns YTD balances keyed by accumulator_code for all CALENDAR_YEAR accumulators
    /// with periods that start in the same calendar year as <paramref name="asOf"/> and
    /// before <paramref name="asOf"/> (i.e., prior completed periods only).
    /// </summary>
    Task<IReadOnlyDictionary<string, decimal>> GetYtdBalancesAsync(Guid employmentId, DateOnly asOf);

    Task UpsertBalanceAsync(AccumulatorBalance balance);
    Task InsertImpactAsync(AccumulatorImpact impact);
    Task InsertContributionAsync(AccumulatorContribution contribution);

    /// <summary>
    /// Atomically writes impact + contribution + balance upsert in a single transaction.
    /// </summary>
    Task ApplyImpactChainAsync(AccumulatorImpact impact, AccumulatorContribution contribution,
        AccumulatorBalance balance, IUnitOfWork uow);

    Task<IReadOnlyList<AccumulatorImpact>>       GetImpactsByRunIdAsync(Guid runId);
    Task<IReadOnlyList<AccumulatorImpact>>       GetImpactsByEmploymentIdAsync(Guid employmentId);
    Task<IReadOnlyList<AccumulatorImpact>>       GetImpactsByResultIdAsync(Guid employeePayrollResultId);
    Task<IReadOnlyList<AccumulatorContribution>> GetContributionsByResultIdAsync(Guid employeePayrollResultId);

    /// <summary>
    /// Sets current_value on the accumulator_balance row identified by
    /// (definition, participant, period) back to <paramref name="targetValue"/>.
    /// Used exclusively by the reversal path.
    /// </summary>
    Task RevertBalanceAsync(Guid accumulatorDefinitionId, Guid employmentId,
        Guid periodId, decimal targetValue, Guid runId, DateTimeOffset now);
}
