namespace AllWorkHRIS.Core.Pipeline;

/// <summary>
/// Empty-result fallback for <see cref="IPayrollHoursSource"/>. Registered by
/// the host so the payroll engine can compose even when the T&amp;A module is
/// not loaded; the T&amp;A module's adapter overrides this via last-registration
/// wins when present.
/// </summary>
public sealed class NullPayrollHoursSource : IPayrollHoursSource
{
    public Task<IReadOnlyList<(DateOnly WorkDate, decimal Hours)>> GetApprovedHoursByEmploymentAndPeriodAsync(
        Guid employmentId, DateOnly periodStart, DateOnly periodEnd)
        => Task.FromResult<IReadOnlyList<(DateOnly WorkDate, decimal Hours)>>([]);
}
