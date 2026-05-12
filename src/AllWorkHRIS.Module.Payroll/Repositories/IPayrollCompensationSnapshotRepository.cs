namespace AllWorkHRIS.Module.Payroll.Repositories;

public sealed record CompensationSnapshot
{
    public decimal? AnnualEquivalent { get; init; }
    public decimal  BaseRate         { get; init; }
    public string   RateTypeCode     { get; init; } = string.Empty;
    public string   FlsaStatusCode   { get; init; } = string.Empty;
}

public interface IPayrollCompensationSnapshotRepository
{
    /// <summary>
    /// Returns the active primary compensation snapshot for the given employment as of the
    /// specified date, including FLSA classification and rate type. Returns null when no active
    /// primary record exists.
    /// </summary>
    Task<CompensationSnapshot?> GetSnapshotAsync(Guid employmentId, DateOnly asOf);
}
