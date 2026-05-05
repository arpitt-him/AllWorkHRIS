namespace AllWorkHRIS.Module.TimeAttendance.Domain;

public sealed record OvertimeDetectionResult
{
    public Guid                IsApplicableFor   { get; init; }
    public bool                IsApplicable      { get; init; }
    public bool                OvertimeDetected  { get; init; }
    public decimal             TotalRegularHours { get; init; }
    public decimal             OvertimeHours     { get; init; }
    public IReadOnlyList<Guid> ReclassifiedEntryIds { get; init; } = [];

    public static OvertimeDetectionResult NotApplicable(Guid employmentId) =>
        new() { IsApplicableFor = employmentId, IsApplicable = false };

    public static OvertimeDetectionResult NoOvertime(Guid employmentId, decimal totalHours) =>
        new() { IsApplicableFor = employmentId, IsApplicable = true, TotalRegularHours = totalHours };

    public static OvertimeDetectionResult WithOvertime(
        Guid employmentId,
        decimal totalHours,
        decimal overtimeHours,
        IReadOnlyList<Guid> reclassifiedIds) =>
        new()
        {
            IsApplicableFor      = employmentId,
            IsApplicable         = true,
            OvertimeDetected     = true,
            TotalRegularHours    = totalHours,
            OvertimeHours        = overtimeHours,
            ReclassifiedEntryIds = reclassifiedIds
        };
}
