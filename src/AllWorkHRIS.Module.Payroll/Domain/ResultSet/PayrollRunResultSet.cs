namespace AllWorkHRIS.Module.Payroll.Domain.ResultSet;

public sealed record PayrollRunResultSet
{
    public Guid        PayrollRunResultSetId           { get; init; }
    public Guid        PayrollRunId                    { get; init; }
    public Guid?       RunScopeId                      { get; init; }
    public Guid        SourcePeriodId                  { get; init; }
    public Guid        ExecutionPeriodId               { get; init; }
    public Guid?       ParentPayrollRunResultSetId     { get; init; }
    public Guid?       RootPayrollRunResultSetId       { get; init; }
    public int?        ResultSetLineageSequence        { get; init; }
    public Guid?       CorrectionReferenceId           { get; init; }
    public int         ResultSetStatusId               { get; init; }
    public int         ResultSetTypeId                 { get; init; }
    public DateTimeOffset? ExecutionStartTimestamp     { get; init; }
    public DateTimeOffset? ExecutionEndTimestamp       { get; init; }
    public bool        ApprovalRequiredFlag            { get; init; }
    public Guid?       ApprovedByUserId                { get; init; }
    public DateTimeOffset? ApprovalTimestamp           { get; init; }
    public DateTimeOffset? FinalizationTimestamp       { get; init; }
    public DateTimeOffset  CreatedTimestamp            { get; init; }
    public DateTimeOffset  UpdatedTimestamp            { get; init; }
}
