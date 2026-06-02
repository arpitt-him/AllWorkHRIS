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
        Guid employmentId, DateOnly periodStart, DateOnly periodEnd, Guid payrollRunId)
        => Task.FromResult<IReadOnlyList<(DateOnly WorkDate, decimal Hours)>>([]);

    public Task LockHoursForRunAsync(
        Guid payrollRunId, IReadOnlyList<Guid> employmentIds, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task UnlockHoursForRunAsync(Guid payrollRunId, CancellationToken ct = default)
        => Task.CompletedTask;
}
