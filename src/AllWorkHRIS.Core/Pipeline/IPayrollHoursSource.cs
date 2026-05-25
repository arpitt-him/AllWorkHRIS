namespace AllWorkHRIS.Core.Pipeline;

/// <summary>
/// Source of approved/locked worked hours used by the payroll calculation engine
/// to compute hours-based pay and FLSA overtime for non-exempt employees.
///
/// Placed in Core so Payroll can consume hours without taking a direct assembly
/// reference on the Time &amp; Attendance module. Implemented by the T&amp;A module
/// when loaded; falls back to <see cref="NullPayrollHoursSource"/> (empty result)
/// when T&amp;A is not in the composition.
/// </summary>
public interface IPayrollHoursSource
{
    /// <summary>
    /// Returns total approved/locked worked hours per calendar date for an
    /// employment within the given pay period date range.
    /// </summary>
    Task<IReadOnlyList<(DateOnly WorkDate, decimal Hours)>> GetApprovedHoursByEmploymentAndPeriodAsync(
        Guid employmentId, DateOnly periodStart, DateOnly periodEnd);
}
