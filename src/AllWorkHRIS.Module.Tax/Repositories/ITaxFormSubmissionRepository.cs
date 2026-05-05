using AllWorkHRIS.Module.Tax.Queries;

namespace AllWorkHRIS.Module.Tax.Repositories;

public interface ITaxFormSubmissionRepository
{
    // Returns the active filing profile for the employee in the given jurisdiction
    // as of payDate, or null if no submission exists (employee not yet filed / exempt).
    Task<EmployeeFilingProfile?> GetActiveProfileAsync(
        Guid     employmentId,
        string   jurisdictionCode,
        DateOnly payDate,
        CancellationToken ct = default);
}
