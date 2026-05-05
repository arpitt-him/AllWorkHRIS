namespace AllWorkHRIS.Module.TimeAttendance.Domain;

public sealed record HandoffResult(
    Guid    PeriodId,
    Guid    PayrollRunId,
    int     Delivered,
    int     Failed,
    decimal TotalHours);
