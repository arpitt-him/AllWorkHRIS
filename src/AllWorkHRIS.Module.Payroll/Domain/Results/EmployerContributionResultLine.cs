namespace AllWorkHRIS.Module.Payroll.Domain.Results;

public sealed record EmployerContributionResultLine
{
    public Guid     EmployerContributionResultLineId { get; init; }
    public Guid     EmployeePayrollResultId          { get; init; }
    public Guid     EmploymentId                     { get; init; }
    public string   ContributionCode                 { get; init; } = default!;
    public string   ContributionDescription          { get; init; } = default!;
    public decimal  CalculatedAmount                 { get; init; }
    public bool     AccumulatorImpactFlag            { get; init; }
    public Guid?    SourceRuleVersionId              { get; init; }
    public bool     CorrectionFlag                   { get; init; }
    public Guid?    CorrectsLineId                   { get; init; }
    public DateTimeOffset CreationTimestamp          { get; init; }
}
