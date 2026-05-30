namespace AllWorkHRIS.Module.Payroll.Domain.Accumulators;

/// <summary>
/// One audit-only record of an accumulator period reset (ADR-020 / Phase 12.9):
/// a closing-balance snapshot at a reset boundary. Balances are not mutated — YTD
/// is derived, so a new boundary starts fresh on its own; this row is purely the
/// §7 audit trail. Written automatically (<see cref="OpenedBy"/> = "SYSTEM") by
/// lazy detection when a run crosses a boundary, or manually (actor identifier).
/// </summary>
public sealed record AccumulatorResetAudit
{
    public Guid     AccumulatorResetAuditId { get; init; }
    public Guid     AccumulatorDefinitionId { get; init; }
    public int      AccumulatorFamilyId     { get; init; }
    public Guid?    ParticipantId           { get; init; }
    public Guid?    LegalEntityId           { get; init; }
    public int      ScopeTypeId             { get; init; }
    public string   ResetType               { get; init; } = default!; // CALENDAR_YEAR | PLAN_YEAR
    public int      ResetBoundaryYear       { get; init; }             // boundary that closed, by start year
    public DateOnly ResetDate               { get; init; }             // calendar date the reset took effect
    public decimal  ClosingBalance          { get; init; }
    public string   OpenedBy                { get; init; } = "SYSTEM";  // "SYSTEM" or actor identifier
    public string   ResetSource             { get; init; } = "AUTOMATIC"; // AUTOMATIC | MANUAL
    public string?  Notes                   { get; init; }
    public Guid     CreatedBy               { get; init; }
    public DateTimeOffset CreationTimestamp { get; init; }
}
