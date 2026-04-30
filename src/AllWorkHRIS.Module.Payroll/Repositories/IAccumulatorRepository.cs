using AllWorkHRIS.Module.Payroll.Domain.Accumulators;

namespace AllWorkHRIS.Module.Payroll.Repositories;

public interface IAccumulatorRepository
{
    Task<AccumulatorDefinition?> GetDefinitionByCodeAsync(string accumulatorCode);
    Task<IReadOnlyList<AccumulatorDefinition>> GetAllActiveDefinitionsAsync();

    Task<AccumulatorBalance?> GetBalanceAsync(Guid accumulatorDefinitionId, Guid? employmentId,
        Guid? legalEntityId, Guid periodId);

    Task UpsertBalanceAsync(AccumulatorBalance balance);
    Task InsertImpactAsync(AccumulatorImpact impact);
    Task InsertContributionAsync(AccumulatorContribution contribution);

    Task<IReadOnlyList<AccumulatorImpact>> GetImpactsByRunIdAsync(Guid runId);
    Task<IReadOnlyList<AccumulatorImpact>> GetImpactsByEmploymentIdAsync(Guid employmentId);
}
