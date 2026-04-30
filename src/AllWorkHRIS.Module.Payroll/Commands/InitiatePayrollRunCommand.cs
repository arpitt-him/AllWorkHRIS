namespace AllWorkHRIS.Module.Payroll.Commands;

public sealed record InitiatePayrollRunCommand
{
    public required Guid     PayrollContextId  { get; init; }
    public required Guid     PeriodId          { get; init; }
    public required int      RunTypeId         { get; init; }
    public string?           RunDescription    { get; init; }
    public Guid?             ParentRunId       { get; init; }
    public required Guid     InitiatedBy       { get; init; }
}
