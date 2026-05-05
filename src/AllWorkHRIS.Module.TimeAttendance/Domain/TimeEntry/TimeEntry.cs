using AllWorkHRIS.Module.TimeAttendance.Commands;

namespace AllWorkHRIS.Module.TimeAttendance.Domain;

public sealed record TimeEntry
{
    public Guid            TimeEntryId          { get; init; }
    public Guid            EmploymentId         { get; init; }
    public Guid            PayrollPeriodId      { get; init; }
    public DateOnly        WorkDate             { get; init; }
    public int             TimeCategoryId       { get; init; }
    public decimal         Duration             { get; init; }
    public TimeOnly?       StartTime            { get; init; }
    public TimeOnly?       EndTime              { get; init; }
    public Guid?           ShiftId              { get; init; }
    public int             StatusId             { get; init; }
    public int             EntryMethodId        { get; init; }
    public Guid            SubmittedBy          { get; init; }
    public DateTimeOffset  SubmittedAt          { get; init; }
    public Guid?           ApprovedBy           { get; init; }
    public DateTimeOffset? ApprovedAt           { get; init; }
    public string?         RejectionReason      { get; init; }
    public Guid?           OriginalTimeEntryId  { get; init; }
    public string?         CorrectionReason     { get; init; }
    public bool            RetroactiveFlag      { get; init; }
    public Guid?           PayrollRunId         { get; init; }
    public string?         Notes                { get; init; }
    public string?         ProjectCode          { get; init; }
    public string?         TaskCode             { get; init; }
    public DateTimeOffset  CreatedAt            { get; init; }
    public DateTimeOffset  UpdatedAt            { get; init; }

    // Resolved via JOIN in repository queries
    public string TimeCategoryCode { get; init; } = string.Empty;
    public string StatusCode       { get; init; } = string.Empty;
    public string EntryMethodCode  { get; init; } = string.Empty;

    public TimeEntryStatus Status   => Enum.Parse<TimeEntryStatus>(StatusCode, ignoreCase: true);
    public TimeCategory    Category => Enum.Parse<TimeCategory>(TimeCategoryCode.Replace("_", ""), ignoreCase: true);
    public EntryMethod     Method   => Enum.Parse<EntryMethod>(EntryMethodCode.Replace("_", ""), ignoreCase: true);

    public static TimeEntry Create(
        SubmitTimeEntryCommand command,
        int statusId,
        int timeCategoryId,
        int entryMethodId) =>
        new()
        {
            TimeEntryId     = Guid.NewGuid(),
            EmploymentId    = command.EmploymentId,
            PayrollPeriodId = command.PayrollPeriodId,
            WorkDate        = command.WorkDate,
            TimeCategoryId  = timeCategoryId,
            Duration        = command.Duration,
            StartTime       = command.StartTime,
            EndTime         = command.EndTime,
            ShiftId         = command.ShiftId,
            StatusId        = statusId,
            EntryMethodId   = entryMethodId,
            SubmittedBy     = command.SubmittedBy,
            SubmittedAt     = DateTimeOffset.UtcNow,
            Notes           = command.Notes,
            ProjectCode     = command.ProjectCode,
            TaskCode        = command.TaskCode,
            CreatedAt       = DateTimeOffset.UtcNow,
            UpdatedAt       = DateTimeOffset.UtcNow
        };

    public static TimeEntry CreateCorrection(
        TimeEntry original,
        CorrectTimeEntryCommand command,
        int submittedStatusId,
        int timeCategoryId) =>
        new()
        {
            TimeEntryId           = Guid.NewGuid(),
            EmploymentId          = original.EmploymentId,
            PayrollPeriodId       = original.PayrollPeriodId,
            WorkDate              = command.WorkDate,
            TimeCategoryId        = timeCategoryId,
            Duration              = command.Duration,
            StartTime             = command.StartTime,
            EndTime               = command.EndTime,
            ShiftId               = original.ShiftId,
            StatusId              = submittedStatusId,
            EntryMethodId         = original.EntryMethodId,
            SubmittedBy           = command.CorrectedBy,
            SubmittedAt           = DateTimeOffset.UtcNow,
            OriginalTimeEntryId   = original.TimeEntryId,
            CorrectionReason      = command.CorrectionReason,
            RetroactiveFlag       = command.RetroactiveFlag,
            CreatedAt             = DateTimeOffset.UtcNow,
            UpdatedAt             = DateTimeOffset.UtcNow
        };
}
