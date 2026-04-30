namespace AllWorkHRIS.Module.Payroll.Domain.Results;

public sealed record TaxResultLine
{
    public Guid     TaxResultLineId           { get; init; }
    public Guid     EmployeePayrollResultId   { get; init; }
    public Guid     EmploymentId              { get; init; }
    public Guid     JurisdictionId            { get; init; }
    public string   TaxCode                   { get; init; } = default!;
    public string   TaxDescription            { get; init; } = default!;
    public decimal  TaxableWagesAmount        { get; init; }
    public decimal  CalculatedAmount          { get; init; }
    public bool     EmployerFlag              { get; init; }
    public bool     AccumulatorImpactFlag     { get; init; }
    public Guid?    SourceRuleVersionId       { get; init; }
    public bool     CorrectionFlag            { get; init; }
    public Guid?    CorrectsLineId            { get; init; }
    public DateTimeOffset CreationTimestamp   { get; init; }
}
