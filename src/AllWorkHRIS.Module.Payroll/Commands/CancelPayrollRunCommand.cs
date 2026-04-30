namespace AllWorkHRIS.Module.Payroll.Commands;

public sealed record CancelPayrollRunCommand
{
    public required Guid   RunId       { get; init; }
    public required Guid   CancelledBy { get; init; }
    public required string Reason      { get; init; }
}
