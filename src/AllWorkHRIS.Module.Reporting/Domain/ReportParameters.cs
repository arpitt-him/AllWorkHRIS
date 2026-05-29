namespace AllWorkHRIS.Module.Reporting.Domain;

/// <summary>
/// Shared parameter shape for all 16 pre-built reports. Each report uses only
/// the fields it needs; the rest stay null. The whole record is serialised to
/// <c>parameters_json</c> on the execution-history row so Re-run can reproduce
/// the original scope exactly.
/// </summary>
public sealed record ReportParameters
{
    // Scope
    public Guid?     RunId             { get; init; }
    public Guid?     PayrollContextId  { get; init; }
    public Guid?     LegalEntityId     { get; init; }
    public Guid?     DepartmentId      { get; init; }
    public Guid?     LocationId        { get; init; }

    // Date
    public DateOnly? AsOfDate          { get; init; }
    public DateOnly? PeriodStart       { get; init; }
    public DateOnly? PeriodEnd         { get; init; }

    // Report-specific
    public string?   JurisdictionLevel { get; init; }   // PAY-RPT-008
    public decimal?  VarianceThreshold { get; init; }   // PAY-RPT-006
    public int?      ExpirationDays    { get; init; }   // HR-RPT-008
    public string?   EmploymentType    { get; init; }   // HR-RPT-001
    public string?   EventType         { get; init; }   // HR-RPT-002

    // Pagination — on-screen view only; exports always fetch all rows
    public int       Page              { get; init; } = 1;
    public int       PageSize          { get; init; } = 100;
}
