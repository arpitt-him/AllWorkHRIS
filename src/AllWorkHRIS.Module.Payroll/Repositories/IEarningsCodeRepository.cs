using AllWorkHRIS.Module.Payroll.Domain.Earnings;

namespace AllWorkHRIS.Module.Payroll.Repositories;

public interface IEarningsCodeRepository
{
    /// <summary>
    /// All active (status = ACTIVE, effective as-of <paramref name="asOf"/>) earnings
    /// codes. The calculation engine loads these once per run to validate the codes it
    /// emits and to resolve each line's taxable / accumulator-impact flags from config
    /// rather than hardcoding them. Phase 12.5.4.
    /// </summary>
    Task<IReadOnlyList<EarningsCode>> GetAllActiveAsync(DateOnly asOf);
}
