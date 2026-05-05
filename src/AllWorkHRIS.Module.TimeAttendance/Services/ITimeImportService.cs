using AllWorkHRIS.Module.TimeAttendance.Domain;

namespace AllWorkHRIS.Module.TimeAttendance.Services;

public interface ITimeImportService
{
    Task<TimeImportResult> ImportAsync(Stream csv, Guid importedBy, Guid? scopedEntityId = null, CancellationToken ct = default);
}
