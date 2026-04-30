namespace AllWorkHRIS.Module.Payroll.Domain.Results;

public sealed record EmployeePayrollResult
{
    public Guid        EmployeePayrollResultId           { get; init; }
    public Guid        PayrollRunResultSetId             { get; init; }
    public Guid        PayrollRunId                      { get; init; }
    public Guid?       RunScopeId                        { get; init; }
    public Guid        EmploymentId                      { get; init; }
    public Guid        PersonId                          { get; init; }
    public Guid        PayrollContextId                  { get; init; }
    public Guid        SourcePeriodId                    { get; init; }
    public Guid        ExecutionPeriodId                 { get; init; }
    public Guid?       ParentEmployeePayrollResultId     { get; init; }
    public Guid?       RootEmployeePayrollResultId       { get; init; }
    public int?        ResultLineageSequence             { get; init; }
    public Guid?       CorrectionReferenceId             { get; init; }
    public int         ResultStatusId                    { get; init; }
    public DateOnly    PayPeriodStartDate                { get; init; }
    public DateOnly    PayPeriodEndDate                  { get; init; }
    public DateOnly    PayDate                           { get; init; }
    public decimal     GrossPayAmount                    { get; init; }
    public decimal     TotalDeductionsAmount             { get; init; }
    public decimal     TotalEmployeeTaxAmount            { get; init; }
    public decimal     TotalEmployerContributionAmount   { get; init; }
    public decimal     NetPayAmount                      { get; init; }
    public DateTimeOffset CreatedTimestamp               { get; init; }
    public DateTimeOffset UpdatedTimestamp               { get; init; }
}
