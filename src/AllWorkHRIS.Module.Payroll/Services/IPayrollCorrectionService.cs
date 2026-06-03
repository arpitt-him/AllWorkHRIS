namespace AllWorkHRIS.Module.Payroll.Services;

/// <summary>
/// ADR-027 — the forward-only correction / re-pay keystone, built as a standalone, callable
/// processing service (decoupled from UI / command plumbing) so the <i>same</i> primitive serves the
/// run-detail "reverse run" action, the ADR-026 D4 workweek-transition true-up, retro / CBA pay
/// (#51), scoped re-pay (#43), and the contemplated EWA settlement. Inputs are plain values and the
/// result is structured, so it is trivially wrappable later as a background job, an API/MCP endpoint,
/// or a Core cross-module seam (the IPayrollContextLookup / IPayrollHoursSource pattern, ADR-011).
///
/// Increment 1 implements <see cref="ReverseRunAsync"/> for an APPROVED run; re-pay / additive
/// supplemental operations are added here in later increments.
/// </summary>
public interface IPayrollCorrectionService
{
    /// <summary>
    /// Reverse an <b>APPROVED</b> (not yet released) payroll run: contra-post every standing employee
    /// result's accumulator impacts (forward-only negating rows — originals are never deleted, so
    /// faithful replay per ADR-002 is preserved), mark each reversed result and the run
    /// <c>REVERSED</c>, and release the run's time-entry locks so the period reopens for a fresh run.
    /// <para>
    /// <b>Idempotent:</b> a run already <c>REVERSED</c> is a no-op (<see cref="ReversalOutcome.AlreadyReversed"/>
    /// = true), and already-reversed results are skipped — so a re-invocation after a partial failure
    /// safely resumes. Reversing a <c>RELEASED</c> run (money already disbursed → reverse + re-pay /
    /// W-2c) is a later increment and currently throws.
    /// </para>
    /// Returns the reversed result ids so a caller can chain a re-pay.
    /// </summary>
    Task<ReversalOutcome> ReverseRunAsync(ReverseRunRequest request, CancellationToken ct = default);

    /// <summary>
    /// Reverse a <b>subset</b> of an APPROVED run's results — the selected employees only (ADR-027,
    /// per-EE granularity). Contra-posts just those results and marks them <c>REVERSED</c>; the run
    /// <b>stays APPROVED</b> while any standing result remains (so the period is still covered by an
    /// active Regular run and a scoped Supplemental can re-pay the reversed employees). If the
    /// selection leaves no standing result, the run is marked REVERSED and its locks released —
    /// i.e. <see cref="ReverseRunAsync"/> is exactly this with every employee selected. Idempotent.
    /// </summary>
    Task<ReversalOutcome> ReverseResultsAsync(ReverseResultsRequest request, CancellationToken ct = default);
}

/// <summary>Plain request for <see cref="IPayrollCorrectionService.ReverseRunAsync"/>.</summary>
public sealed record ReverseRunRequest
{
    public required Guid   RunId      { get; init; }
    public required Guid   ReversedBy { get; init; }
    public required string Reason     { get; init; }
}

/// <summary>Plain request for <see cref="IPayrollCorrectionService.ReverseResultsAsync"/> — reverse
/// only the results belonging to <see cref="EmploymentIds"/> within the run.</summary>
public sealed record ReverseResultsRequest
{
    public required Guid                     RunId         { get; init; }
    public required IReadOnlyCollection<Guid> EmploymentIds { get; init; }
    public required Guid                     ReversedBy    { get; init; }
    public required string                   Reason        { get; init; }
}

/// <summary>Structured outcome — lets a caller chain (e.g. a re-pay over the reversed results).</summary>
public sealed record ReversalOutcome
{
    public required Guid                RunId             { get; init; }
    /// <summary>True when the run was already REVERSED and nothing was done (idempotent no-op).</summary>
    public required bool                AlreadyReversed   { get; init; }
    public required IReadOnlyList<Guid> ReversedResultIds { get; init; }
}
