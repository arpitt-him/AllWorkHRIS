namespace AllWorkHRIS.Module.TimeAttendance.Domain;

public sealed record TimeImportResult(
    int Imported,
    int Failed,
    IReadOnlyList<TimeImportError> Errors);

public sealed record TimeImportError(
    int    RowNumber,
    string EmployeeNumber,
    string Reason);
