namespace AllWorkHRIS.Host.Hris.Commands;

public sealed record HireEmployeeCommand
{
    // Person identity
    public required string   LegalFirstName       { get; init; }
    public required string   LegalLastName        { get; init; }
    public string?           LegalMiddleName      { get; init; }
    public string?           PreferredName        { get; init; }
    public required DateOnly DateOfBirth          { get; init; }
    public required string   NationalIdentifier   { get; init; }

    // Employment
    public required Guid     LegalEntityId        { get; init; }
    public required string   EmployeeNumber       { get; init; }
    public required int      EmploymentTypeId     { get; init; }
    public required DateOnly EmploymentStartDate  { get; init; }
    public required int      FlsaStatusId         { get; init; }
    public required int      FullPartTimeStatusId { get; init; }
    public Guid?             PayrollContextId     { get; init; }

    // Initial assignment
    public required Guid     JobId                { get; init; }
    public Guid?             PositionId           { get; init; }
    public required Guid     DepartmentId         { get; init; }
    public required Guid     LocationId           { get; init; }
    public Guid?             ManagerEmploymentId  { get; init; }

    // Initial compensation
    public required int      RateTypeId           { get; init; }
    public required decimal  BaseRate             { get; init; }
    public required int      PayFrequencyId       { get; init; }
    public required string   ChangeReasonCode     { get; init; }

    // Contact
    public required string   AddressLine1         { get; init; }
    public string?           AddressLine2         { get; init; }
    public required string   City                 { get; init; }
    public required string   StateCode            { get; init; }
    public required string   PostalCode           { get; init; }
    public string            CountryCode          { get; init; } = "US";
    public string?           PhonePrimary         { get; init; }
    public string?           EmailPersonal        { get; init; }

    // Metadata
    public required Guid     InitiatedBy          { get; init; }
}

public sealed record RehireEmployeeCommand
{
    public required Guid     PersonId             { get; init; }
    public required Guid     LegalEntityId        { get; init; }
    public required string   EmployeeNumber       { get; init; }
    public required int      EmploymentTypeId     { get; init; }
    public required DateOnly EmploymentStartDate  { get; init; }
    public required int      FlsaStatusId         { get; init; }
    public required int      FullPartTimeStatusId { get; init; }
    public Guid?             PayrollContextId     { get; init; }
    public required Guid     JobId                { get; init; }
    public Guid?             PositionId           { get; init; }
    public required Guid     DepartmentId         { get; init; }
    public required Guid     LocationId           { get; init; }
    public Guid?             ManagerEmploymentId  { get; init; }
    public required int      RateTypeId           { get; init; }
    public required decimal  BaseRate             { get; init; }
    public required int      PayFrequencyId       { get; init; }
    public required string   ChangeReasonCode     { get; init; }
    public required Guid     InitiatedBy          { get; init; }
}

public sealed record TerminateEmployeeCommand
{
    public required Guid     EmploymentId         { get; init; }
    public required DateOnly TerminationDate      { get; init; }
    public required string   ReasonCode           { get; init; }
    public required Guid     InitiatedBy          { get; init; }
    public string?           Notes                { get; init; }
}

public sealed record ChangeCompensationCommand
{
    public required Guid     EmploymentId         { get; init; }
    public required int      RateTypeId           { get; init; }
    public required decimal  NewBaseRate          { get; init; }
    public required int      PayFrequencyId       { get; init; }
    public required DateOnly EffectiveDate        { get; init; }
    public required string   ChangeReasonCode     { get; init; }
    public required Guid     InitiatedBy          { get; init; }
}

public sealed record TransferEmployeeCommand
{
    public required Guid     EmploymentId         { get; init; }
    public required Guid     NewDepartmentId      { get; init; }
    public required Guid     NewLocationId        { get; init; }
    public Guid?             NewJobId             { get; init; }
    public Guid?             NewPositionId        { get; init; }
    public required DateOnly EffectiveDate        { get; init; }
    public required string   ReasonCode           { get; init; }
    public required Guid     InitiatedBy          { get; init; }
    public string?           Notes                { get; init; }
}

public sealed record UpdatePersonCommand
{
    public required Guid     PersonId             { get; init; }
    public string?           PreferredName        { get; init; }
    public string?           Gender               { get; init; }
    public string?           Pronouns             { get; init; }
    public string?           MaritalStatus        { get; init; }
    public string?           LanguagePreference   { get; init; }
    public string?           VeteranStatus        { get; init; }
    public string?           DisabilityStatus     { get; init; }
    public required Guid     InitiatedBy          { get; init; }
}

public sealed record ChangeManagerCommand
{
    public required Guid     EmploymentId           { get; init; }
    public required Guid     NewManagerEmploymentId { get; init; }
    public required DateOnly EffectiveDate          { get; init; }
    public required Guid     InitiatedBy            { get; init; }
    public string?           Notes                  { get; init; }
}
