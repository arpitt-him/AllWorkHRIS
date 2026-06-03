namespace AllWorkHRIS.Module.Payroll.Commands;

/// <summary>ADR-027 2a.3 — reverse a selected subset of an approved run's employees.</summary>
public sealed record ReverseEmploymentResultsCommand
{
    public required Guid                      RunId         { get; init; }
    public required IReadOnlyCollection<Guid> EmploymentIds { get; init; }
    public required Guid                      ReversedBy    { get; init; }
    public required string                    Reason        { get; init; }
}
