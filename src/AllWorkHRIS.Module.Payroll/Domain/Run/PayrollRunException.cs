namespace AllWorkHRIS.Module.Payroll.Domain.Run;

public sealed record PayrollRunException
{
    public Guid            RunExceptionId   { get; init; }
    public Guid            RunId            { get; init; }
    public Guid            EmploymentId     { get; init; }
    public string          ExceptionCode    { get; init; } = default!;
    public string?         ExceptionMessage { get; init; }
    public DateTimeOffset  CreatedTimestamp { get; init; }
}
