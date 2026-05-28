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
    string         Status);
