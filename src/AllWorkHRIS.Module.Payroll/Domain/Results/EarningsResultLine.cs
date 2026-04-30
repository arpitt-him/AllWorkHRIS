namespace AllWorkHRIS.Module.Payroll.Domain.Results;

public sealed record EarningsResultLine
{
    public Guid     EarningsResultLineId      { get; init; }
    public Guid     EmployeePayrollResultId   { get; init; }
    public Guid     EmploymentId              { get; init; }
    public string   EarningsCode              { get; init; } = default!;
    public string   EarningsDescription       { get; init; } = default!;
    public decimal? Quantity                  { get; init; }
    public decimal? Rate                      { get; init; }
    public decimal  CalculatedAmount          { get; init; }
    public bool     JurisdictionSplitFlag     { get; init; }
    public bool     TaxableFlag               { get; init; }
    public bool     AccumulatorImpactFlag     { get; init; }
    public Guid?    SourceRuleVersionId       { get; init; }
    public bool     CorrectionFlag            { get; init; }
    public Guid?    CorrectsLineId            { get; init; }
    public DateTimeOffset CreationTimestamp   { get; init; }
}
