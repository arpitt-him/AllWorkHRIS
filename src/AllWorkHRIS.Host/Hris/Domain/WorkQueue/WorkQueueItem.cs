namespace AllWorkHRIS.Host.Hris.Domain;

public sealed record WorkQueueItem
{
    public Guid    WorkQueueItemId  { get; init; }
    public string  ItemType         { get; init; } = default!;
    public Guid    ReferenceId      { get; init; }
    public string  ReferenceType    { get; init; } = default!;
    public Guid?   EmploymentId     { get; init; }
    public string  AssignedRole     { get; init; } = default!;
    public string  Status           { get; init; } = "OPEN";
    public string  Priority         { get; init; } = "NORMAL";
    public string  Title            { get; init; } = default!;
    public string? Description      { get; init; }
    public DateOnly? DueDate        { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
    public Guid?   ResolvedBy       { get; init; }
}

public static class WorkQueueItemTypes
{
    public const string LeaveApproval    = "LEAVE_APPROVAL";
    public const string DocExpiring90    = "DOC_EXPIRING_90";
    public const string DocExpiring30    = "DOC_EXPIRING_30";
    public const string DocExpired       = "DOC_EXPIRED";
    public const string OnboardingTask   = "ONBOARDING_TASK";
}

public static class WorkQueuePriority
{
    public const string Normal = "NORMAL";
    public const string High   = "HIGH";
    public const string Hold   = "HOLD";
}
