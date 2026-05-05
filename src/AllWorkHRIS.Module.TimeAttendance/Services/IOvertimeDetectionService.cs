using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.TimeAttendance.Domain;

namespace AllWorkHRIS.Module.TimeAttendance.Services;

public interface IOvertimeDetectionService
{
    Task<OvertimeDetectionResult> DetectAndReclassifyAsync(
        Guid employmentId, DateOnly workweekStart, IUnitOfWork uow);
}
