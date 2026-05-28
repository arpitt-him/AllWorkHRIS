using AllWorkHRIS.Module.TimeAttendance.Domain;

namespace AllWorkHRIS.Module.TimeAttendance.Services;

public interface ITimeImportService
{
    Task<TimeImportResult> ImportAsync(
        Stream csv,
        Guid   importedBy,
        string importedByName,
        string fileName,
        Guid?  scopedEntityId = null,
        CancellationToken ct = default);

    /// <summary>
    /// History of committed time-entry imports for a legal entity, newest first.
    /// Only imports that wrote ≥ 1 entry are recorded.
    /// </summary>
    Task<IReadOnlyList<TimeImportHistoryRow>> GetImportHistoryAsync(
        Guid legalEntityId, CancellationToken ct = default);
}
