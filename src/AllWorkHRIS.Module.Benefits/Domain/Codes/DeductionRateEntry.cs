namespace AllWorkHRIS.Module.Benefits.Domain.Codes;

public sealed record DeductionRateEntry
{
    public Guid     RateEntryId  { get; init; }
    public Guid     RateTableId  { get; init; }
    public string?  TierCode     { get; init; }   // EE_ONLY | EE_SPOUSE | EE_CHILD | FAMILY
    public decimal? BandMin      { get; init; }
    public decimal? BandMax      { get; init; }
    public decimal  EmployeeRate { get; init; }
    public decimal? EmployerRate { get; init; }
}
