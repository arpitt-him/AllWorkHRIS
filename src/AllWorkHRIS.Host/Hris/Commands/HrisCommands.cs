namespace AllWorkHRIS.Host.Hris.Commands;

public sealed record HireEmployeeCommand
{
    // Person identity
    public required string    LegalFirstName       { get; init; }
    public required string    LegalLastName        { get; init; }
    public string?            LegalMiddleName      { get; init; }
    public string?            PreferredName        { get; init; }
    public required DateOnly  DateOfBirth          { get; init; }
    public required string    NationalIdentifier   { get; init; }  // Encrypted at rest

    // Employment
    public required Guid      LegalEntityId        { get; init; }
    public required string    EmployeeNumber       { get; init; }
    public required string    EmploymentType       { get; init; }  // EMPLOYEE, CONTRACTOR, etc.
    public required DateOnly  EmploymentStartDate  { get; init; }
    public required string    FlsaStatus           { get; init; }  // EXEMPT / NON_EXEMPT
    public required string    FullOrPartTimeStatus { get; init; }
    public Guid?              PayrollContextId     { get; init; }
    // Null in HRIS-only deployments where Payroll module is not present.

    // Initial assignment
    public required Guid      JobId                { get; init; }
    public Guid?              PositionId           { get; init; }
    public required Guid      DepartmentId         { get; init; }
    public required Guid      LocationId           { get; init; }
    public Guid?              ManagerEmploymentId  { get; init; }

    // Initial compensation
    public required string    RateType             { get; init; }  // HOURLY, SALARY, etc.
    public required decimal   BaseRate             { get; init; }
    public required string    PayFrequency         { get; init; }
    public required string    ChangeReasonCode     { get; init; }

    // Metadata
    public required Guid      InitiatedBy          { get; init; }
}

public sealed record RehireEmployeeCommand
{
    public required Guid      PersonId             { get; init; }
    public required Guid      LegalEntityId        { get; init; }
    public required string    EmployeeNumber       { get; init; }
    public required string    EmploymentType       { get; init; }
    public required DateOnly  EmploymentStartDate  { get; init; }
    public required string    FlsaStatus           { get; init; }
    public required string    FullOrPartTimeStatus { get; init; }
    public Guid?              PayrollContextId     { get; init; }
    public required Guid      JobId                { get; init; }
    public Guid?              PositionId           { get; init; }
    public required Guid      DepartmentId         { get; init; }
    public required Guid      LocationId           { get; init; }
    public Guid?              ManagerEmploymentId  { get; init; }
    public required string    RateType             { get; init; }
    public required decimal   BaseRate             { get; init; }
    public required string    PayFrequency         { get; init; }
    public required string    ChangeReasonCode     { get; init; }
    public required Guid      InitiatedBy          { get; init; }
}

public sealed record TerminateEmployeeCommand
{
    public required Guid      EmploymentId         { get; init; }
    public required DateOnly  TerminationDate      { get; init; }
    public required string    EventType            { get; init; }  // TERMINATION or VOLUNTARY_RESIGNATION
    public required string    ReasonCode           { get; init; }
    public required Guid      InitiatedBy          { get; init; }
    public string?            Notes                { get; init; }
}

public sealed record ChangeCompensationCommand
{
    public required Guid      EmploymentId         { get; init; }
    public required string    RateType             { get; init; }
    public required decimal   NewBaseRate          { get; init; }
    public required string    PayFrequency         { get; init; }
    public required DateOnly  EffectiveDate        { get; init; }
    public required string    ChangeReasonCode     { get; init; }
    public required Guid      InitiatedBy          { get; init; }
}

public sealed record TransferEmployeeCommand
{
    public required Guid      EmploymentId         { get; init; }
    public required Guid      NewDepartmentId      { get; init; }
    public required Guid      NewLocationId        { get; init; }
    public Guid?              NewJobId             { get; init; }
    public Guid?              NewPositionId        { get; init; }
    public required DateOnly  EffectiveDate        { get; init; }
    public required string    ReasonCode           { get; init; }
    public required Guid      InitiatedBy          { get; init; }
    public string?            Notes                { get; init; }
}

public sealed record UpdatePersonCommand
{
    public required Guid      PersonId             { get; init; }
    public string?            PreferredName        { get; init; }
    public string?            Gender               { get; init; }
    public string?            Pronouns             { get; init; }
    public string?            MaritalStatus        { get; init; }
    public string?            LanguagePreference   { get; init; }
    public string?            VeteranStatus        { get; init; }
    public string?            DisabilityStatus     { get; init; }
    public required Guid      InitiatedBy          { get; init; }
}

public sealed record ChangeManagerCommand
{
    public required Guid      EmploymentId         { get; init; }
    public required Guid      NewManagerEmploymentId { get; init; }
    public required DateOnly  EffectiveDate        { get; init; }
    public required Guid      InitiatedBy          { get; init; }
    public string?            Notes                { get; init; }
}
