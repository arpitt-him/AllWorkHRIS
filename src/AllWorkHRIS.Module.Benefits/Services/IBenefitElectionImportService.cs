using AllWorkHRIS.Module.Benefits.Queries;

namespace AllWorkHRIS.Module.Benefits.Services;

public interface IBenefitElectionImportService
{
    Task<BatchValidationResult> ValidateBatchAsync(Stream fileContent, string fileFormat, CancellationToken ct = default);

    Task<Guid> SubmitBatchAsync(
        Stream fileContent,
        string fileFormat,
        Guid   submittedBy,
        string importedByName,
        string fileName,
        Guid?  legalEntityId,
        CancellationToken ct = default);

    /// <summary>
    /// History of committed benefit-election imports for a legal entity, newest first.
    /// Only imports that created ≥ 1 election are recorded.
    /// </summary>
    Task<IReadOnlyList<BenefitImportHistoryRow>> GetImportHistoryAsync(
        Guid legalEntityId, CancellationToken ct = default);
}
