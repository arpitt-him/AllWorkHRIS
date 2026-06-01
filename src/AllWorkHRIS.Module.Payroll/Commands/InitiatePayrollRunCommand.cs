namespace AllWorkHRIS.Module.Payroll.Commands;

public sealed record InitiatePayrollRunCommand
{
    public required Guid     PayrollContextId  { get; init; }
    public required Guid     PeriodId          { get; init; }
    public required int      RunTypeId         { get; init; }
    public string?           RunDescription    { get; init; }
    public Guid?             ParentRunId       { get; init; }
    public required Guid     InitiatedBy       { get; init; }

    // Phase 12.6 — scoped/targeted (non-Regular) runs. When TargetEmploymentIds is non-empty the
    // service creates a run_scope row defining this subset and links the run to it; the run then
    // pays only these employees. TriggerReason is required for a scoped run; ExceptionDerived marks
    // a "carry over the parent run's exceptions" population. Empty/null = full-context run.
    public IReadOnlyList<Guid>? TargetEmploymentIds { get; init; }
    public string?              TriggerReason       { get; init; }
    public bool                 ExceptionDerived    { get; init; }
}
