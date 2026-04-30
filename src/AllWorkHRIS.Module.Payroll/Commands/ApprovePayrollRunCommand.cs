namespace AllWorkHRIS.Module.Payroll.Commands;

public sealed record ApprovePayrollRunCommand
{
    public required Guid RunId      { get; init; }
    public required Guid ApprovedBy { get; init; }
    public string?       Notes      { get; init; }
}
