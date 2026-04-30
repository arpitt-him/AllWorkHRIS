namespace AllWorkHRIS.Module.Payroll.Domain.Results;

public sealed record DeductionResultLine
{
    public Guid     DeductionResultLineId     { get; init; }
    public Guid     EmployeePayrollResultId   { get; init; }
    public Guid     EmploymentId              { get; init; }
    public string   DeductionCode             { get; init; } = default!;
    public string   DeductionDescription      { get; init; } = default!;
    public decimal  CalculatedAmount          { get; init; }
    public bool     PreTaxFlag                { get; init; }
    public bool     CashImpactFlag            { get; init; }
    public bool     AccumulatorImpactFlag     { get; init; }
    public Guid?    SourceRuleVersionId       { get; init; }
    public bool     CorrectionFlag            { get; init; }
    public Guid?    CorrectsLineId            { get; init; }
    public DateTimeOffset CreationTimestamp   { get; init; }
}
