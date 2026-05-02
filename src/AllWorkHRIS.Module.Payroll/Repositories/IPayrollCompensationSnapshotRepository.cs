namespace AllWorkHRIS.Module.Payroll.Repositories;

public interface IPayrollCompensationSnapshotRepository
{
    /// <summary>
    /// Returns the annual equivalent salary for the given employment as of the specified date,
    /// or null if no active primary compensation record exists.
    /// </summary>
    Task<decimal?> GetAnnualEquivalentAsync(Guid employmentId, DateOnly asOf);
}
