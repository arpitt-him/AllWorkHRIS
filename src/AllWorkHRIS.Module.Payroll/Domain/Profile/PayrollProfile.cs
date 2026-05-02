namespace AllWorkHRIS.Module.Payroll.Domain.Profile;

public sealed record PayrollProfile
{
    public required Guid            PayrollProfileId      { get; init; }
    public required Guid            EmploymentId          { get; init; }
    public required Guid            PersonId              { get; init; }
    public required Guid            PayrollContextId      { get; init; }
    public required string          EnrollmentStatus      { get; init; }  // ACTIVE | SUSPENDED | FINAL_PAY_PENDING | TERMINATED
    public required DateOnly        EffectiveStartDate    { get; init; }
    public          DateOnly?       EffectiveEndDate      { get; init; }
    public required bool            FinalPayFlag          { get; init; }
    public required bool            BlockingTasksCleared  { get; init; }
    public required string          EnrollmentSource      { get; init; }  // AUTO_HIRE | MANUAL
    public required Guid            CreatedBy             { get; init; }
    public required DateTimeOffset  CreationTimestamp     { get; init; }
    public required Guid            LastUpdatedBy         { get; init; }
    public required DateTimeOffset  LastUpdateTimestamp   { get; init; }
}
