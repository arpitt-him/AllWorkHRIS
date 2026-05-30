namespace AllWorkHRIS.Module.Payroll.Commands;

/// <summary>
/// Manually re-enqueue a run that is stuck in an idempotent transient state
/// (APPROVING or RELEASING) without waiting for a host restart. ADR-017 /
/// Phase 12.5.x — the in-UI counterpart to PayrollRunJob's host-startup
/// recovery: same re-enqueue, on demand. No status change; the background
/// job's run- and per-result-level idempotency guards make re-processing safe.
/// </summary>
public sealed record ResumePayrollRunCommand
{
    public required Guid RunId     { get; init; }
    public required Guid ResumedBy { get; init; }
}
