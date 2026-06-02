namespace AllWorkHRIS.Module.TimeAttendance.Domain;

public sealed record TimeImportResult(
    int Imported,
    int Failed,
    IReadOnlyList<TimeImportError> Errors);

public sealed record TimeImportError(
    int    RowNumber,
    string EmployeeNumber,
    string Reason);

public sealed record TimeImportHistoryRow(
    string   FileName,
    DateTime ImportedAt,
    string   ImportedBy,
    int            TotalRows,
    int            AcceptedCount,
    int            RejectedCount,
    string         Status,
    // Phase 12.7c — the pay period of the accepted rows. When the file spanned more than one period
    // the stored period id is null, so these come back null and the UI shows "Multiple".
    int?           PeriodYear,
    int?           PeriodNumber,
    DateOnly?      PayDate)
{
    /// <summary>Compact period token for the history line: "2026 P2 — pay 01/30/2026", or "Multiple".</summary>
    public string PeriodLabel => PeriodYear is int y && PeriodNumber is int n
        ? $"{y} P{n}{(PayDate is DateOnly d ? $" — pay {d:MM/dd/yyyy}" : "")}"
        : "Multiple";
}
