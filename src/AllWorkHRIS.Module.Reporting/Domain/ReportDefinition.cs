namespace AllWorkHRIS.Module.Reporting.Domain;

/// <summary>
/// Catalogue metadata for one report — registered in the report registry,
/// surfaced to the hub pages, and consumed by the exporters.
/// </summary>
/// <param name="ReportId">Stable identifier e.g. "PAY-RPT-001".</param>
/// <param name="Title">Full display title used in headers and history rows.</param>
/// <param name="ShortName">Short label used as the XLSX worksheet name (≤ 31 chars).</param>
/// <param name="Description">One-line description for the hub card.</param>
/// <param name="AllowedRoles">Roles that may run this report. Empty = no role may.</param>
/// <param name="ShowTotals">XLSX exporter renders a totals row for numeric columns when true.</param>
/// <param name="IsWideReport">PDF exporter switches to landscape A4 when true.</param>
public sealed record ReportDefinition(
    string                    ReportId,
    string                    Title,
    string                    ShortName,
    string                    Description,
    IReadOnlyList<string>     AllowedRoles,
    IReadOnlyList<ReportColumn> Columns,
    bool                      ShowTotals     = false,
    bool                      IsWideReport   = false,
    ReportParameterShape      ParameterShape = ReportParameterShape.Run);

/// <summary>
/// Shape of the parameter form for a report — drives which input controls
/// the generic report page renders.
/// </summary>
public enum ReportParameterShape
{
    Run,         // Single payroll Run picker (default — most payroll reports)
    AsOfDate,    // Single point-in-time date
    Period,      // Start + end date range
    Window       // Window-in-days integer (e.g. document expiration window)
}
