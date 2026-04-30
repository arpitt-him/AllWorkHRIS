namespace AllWorkHRIS.Module.Payroll.Commands;

public sealed record ReleasePayrollRunCommand
{
    public required Guid RunId      { get; init; }
    public required Guid ReleasedBy { get; init; }
    public string?       Notes      { get; init; }
}
