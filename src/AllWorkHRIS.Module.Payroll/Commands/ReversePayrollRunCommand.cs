namespace AllWorkHRIS.Module.Payroll.Commands;

public sealed record ReversePayrollRunCommand
{
    public required Guid   RunId      { get; init; }
    public required Guid   ReversedBy { get; init; }
    public required string Reason     { get; init; }
}
