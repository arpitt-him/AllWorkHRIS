namespace AllWorkHRIS.Module.Reporting.Domain;

/// <summary>
/// Result of an export. <see cref="Content"/> is the file bytes ready to stream
/// to the browser; <see cref="MimeType"/> drives the HTTP Content-Type;
/// <see cref="FileName"/> is the suggested download name; <see cref="ExecutionId"/>
/// correlates to the <see cref="ReportExecutionHistory"/> row written for this
/// export.
/// </summary>
public sealed record ExportResult(
    Stream Content,
    string MimeType,
    string FileName,
    Guid   ExecutionId);
