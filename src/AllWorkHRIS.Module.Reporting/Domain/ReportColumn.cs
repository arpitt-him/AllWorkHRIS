namespace AllWorkHRIS.Module.Reporting.Domain;

/// <summary>
/// Describes one column of a report's tabular result for both on-screen display and exports.
/// </summary>
/// <param name="Field">Row-dictionary key (typically the SQL column name).</param>
/// <param name="Header">Display header for grid / XLSX / PDF / CSV.</param>
/// <param name="Format">Logical format tag: "currency", "number", "date", or null for plain text. Exporters key off this.</param>
/// <param name="SyncfusionFormat">Optional Syncfusion-grid format string (e.g. "C2", "yyyy-MM-dd").</param>
/// <param name="Alignment">"Left", "Right", "Center". Drives both grid TextAlign and PDF alignment.</param>
public sealed record ReportColumn(
    string  Field,
    string  Header,
    string? Format          = null,
    string? SyncfusionFormat = null,
    string  Alignment       = "Left");
