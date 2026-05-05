using AllWorkHRIS.Module.Benefits.Queries;

namespace AllWorkHRIS.Module.Benefits.Services;

public interface IBenefitElectionImportService
{
    Task<BatchValidationResult> ValidateBatchAsync(Stream fileContent, string fileFormat, CancellationToken ct = default);
    Task<Guid>                  SubmitBatchAsync(Stream fileContent, string fileFormat, Guid submittedBy, CancellationToken ct = default);
}
