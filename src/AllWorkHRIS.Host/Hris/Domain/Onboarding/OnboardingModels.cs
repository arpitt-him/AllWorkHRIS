namespace AllWorkHRIS.Host.Hris.Domain;

public sealed record OnboardingPlan
{
    public Guid     OnboardingPlanId    { get; init; }
    public Guid     EmploymentId        { get; init; }
    public Guid?    PlanTemplateId      { get; init; }
    public int      PlanStatusId        { get; init; }
    public DateOnly TargetStartDate     { get; init; }
    public DateOnly? CompletionDate     { get; init; }
    public Guid?    AssignedHrContactId { get; init; }
    public Guid     CreatedBy           { get; init; }
    public DateTimeOffset CreationTimestamp   { get; init; }
    public DateTimeOffset LastUpdateTimestamp { get; init; }
    public Guid     LastUpdatedBy       { get; init; }
}

public sealed record OnboardingTask
{
    public Guid     TaskId             { get; init; }
    public Guid     OnboardingPlanId   { get; init; }
    public int      TaskTypeId         { get; init; }
    public string   TaskName           { get; init; } = default!;
    public string   TaskOwnerRole      { get; init; } = default!;
    public Guid?    TaskOwnerUserId    { get; init; }
    public DateOnly DueDate            { get; init; }
    public DateOnly? CompletionDate    { get; init; }
    public int      TaskStatusId       { get; init; }
    public bool     BlockingFlag       { get; init; }
    public string?  WaiverReason       { get; init; }
    public Guid?    WaivedBy           { get; init; }
    public Guid     CreatedBy          { get; init; }
    public DateTimeOffset CreationTimestamp { get; init; }
}
