namespace AllWorkHRIS.Module.Payroll.Domain.Run;

/// <summary>
/// Phase 12.6 — defines the targeted population for a non-Regular (supplemental / adjustment /
/// correction) run, so it pays only a chosen subset instead of re-running the full payroll
/// context. Parented to the approved Regular run via <see cref="ParentRunId"/>; the new scoped
/// run links back via <c>payroll_run.run_scope_id</c>.
///
/// <see cref="PopulationDefinition"/> holds a JSON array of employment_id strings (the resolved
/// target list) for the EXPLICIT / EXCEPTION population methods; the resolver validates every id
/// belongs to the run's payroll context before calculating.
/// </summary>
public sealed record RunScope
{
    public Guid     RunScopeId             { get; init; }
    public Guid     ParentRunId            { get; init; }
    public Guid     PayrollContextId       { get; init; }
    public int      ScopeTypeId            { get; init; }   // lkp_scope_type:        FULL | CATCH_UP | RETRO | RECOVERY
    public int      ScopeStatusId          { get; init; }   // lkp_scope_status:      DRAFT | VALIDATED | READY | RUNNING | COMPLETED | FAILED | CANCELLED
    public string   TriggerReason          { get; init; } = "";
    public int      PopulationMethodId     { get; init; }   // lkp_population_method: EXPLICIT | QUERY | EXCEPTION
    public string   PopulationDefinition   { get; init; } = "[]";  // JSON array of employment_id strings (EXPLICIT/EXCEPTION)
    public int      PopulationCount        { get; init; }
    public bool     ExceptionDerivedFlag   { get; init; }
    public string   PriorityLevel          { get; init; } = "STANDARD";  // STANDARD | CATCH_UP | RECOVERY | EMERGENCY
    public bool     AdjustmentFlag         { get; init; }
    public Guid     CreatedBy              { get; init; }
    public DateTimeOffset CreationTimestamp { get; init; }
}
