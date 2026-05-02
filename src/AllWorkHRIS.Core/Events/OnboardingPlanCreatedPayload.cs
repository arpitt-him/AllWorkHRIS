namespace AllWorkHRIS.Core.Events;

public sealed class OnboardingPlanCreatedPayload
{
    public required Guid           OnboardingPlanId  { get; init; }
    public required Guid           EmploymentId      { get; init; }
    public required Guid           TenantId          { get; init; }
    public required DateOnly       TargetStartDate   { get; init; }
    public required DateTimeOffset EventTimestamp    { get; init; }
    /// <summary>
    /// True when the plan contains at least one task with blocking_flag = true.
    /// False means the employee is immediately eligible for payroll inclusion.
    /// </summary>
    public required bool           HasBlockingTasks  { get; init; }
}
