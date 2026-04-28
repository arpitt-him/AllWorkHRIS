namespace AllWorkHRIS.Host.Hris.Domain;

public enum PersonStatus
{
    Active,
    Inactive,
    Deceased,
    Restricted,
    Archived
}

public enum EmploymentType
{
    Employee,
    Contractor,
    Intern,
    Seasonal
}

public enum EmploymentStatus
{
    Pending,
    Active,
    OnLeave,
    Suspended,
    Terminated,
    Closed
}

public enum FullPartTimeStatus
{
    FullTime,
    PartTime
}

public enum RegularTemporaryStatus
{
    Regular,
    Temporary,
    Seasonal
}

public enum FlsaStatus
{
    Exempt,
    NonExempt
}

public enum AssignmentType
{
    Primary,
    Secondary,
    Temporary,
    Supplemental,
    Override
}

public enum AssignmentStatus
{
    Active,
    Pending,
    Closed,
    Cancelled
}

public enum CompensationRateType
{
    Hourly,
    Salary,
    Commission,
    Contract,
    Differential
}

public enum CompensationStatus
{
    Pending,
    Active,
    Closed,
    Cancelled,
    Superseded
}

public enum PayFrequency
{
    Weekly,
    Biweekly,
    SemiMonthly,
    Monthly
}

public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected
}

public enum OrgUnitType
{
    LegalEntity,
    Division,
    BusinessUnit,
    Department,
    CostCenter,
    Location,
    Region
}

public enum OrgStatus
{
    Active,
    Inactive,
    Archived
}

public enum WorkLocationType
{
    Office,
    Remote,
    Hybrid
}

public enum JobStatus
{
    Active,
    Inactive
}

public enum FlsaClassification
{
    Exempt,
    NonExempt,
    IndependentContractor
}

public enum EeoCategory
{
    ExecSeniorOfficials,
    FirstMidOfficials,
    Professionals,
    Technicians,
    Sales,
    AdminSupport,
    CraftWorkers,
    Operatives,
    Laborers,
    ServiceWorkers
}

public enum PositionStatus
{
    Open,
    Filled,
    Frozen,
    Closed
}
