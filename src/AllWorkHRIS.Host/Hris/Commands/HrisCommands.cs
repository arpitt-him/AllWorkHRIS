namespace AllWorkHRIS.Host.Hris.Commands;

public sealed record HireEmployeeCommand
{
    // Person identity
    public required string   LegalFirstName       { get; init; }
    public required string   LegalLastName        { get; init; }
    public string?           LegalMiddleName      { get; init; }
    public string?           PreferredName        { get; init; }
    public string?           Gender               { get; init; }
    public string?           Pronouns             { get; init; }
    public string?           MaritalStatus        { get; init; }
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
    public required string   LegalFirstName       { get; init; } = default!;
    public string?           LegalMiddleName      { get; init; }
    public required string   LegalLastName        { get; init; } = default!;
    public string?           NameSuffix           { get; init; }
    public required DateOnly DateOfBirth          { get; init; }
    public string?           PreferredName        { get; init; }
    public string?           Gender               { get; init; }
    public string?           Pronouns             { get; init; }
    public string?           MaritalStatus        { get; init; }
    public string?           LanguagePreference      { get; init; }
    public string?           VeteranStatus           { get; init; }
    public string?           DisabilityStatus        { get; init; }
    public string?           NationalIdentifier      { get; init; }
    public string?           NationalIdentifierType  { get; init; }
    public required Guid     InitiatedBy             { get; init; }
}

public sealed record SubmitPersonChangeRequestCommand
{
    public required Guid   PersonId           { get; init; }
    public required string ChangeType         { get; init; }
    public required string CurrentValueJson   { get; init; }
    public required string RequestedValueJson { get; init; }
    public required Guid   RequestedBy        { get; init; }
}

public sealed record ReviewPersonChangeRequestCommand
{
    public required Guid    PersonChangeRequestId { get; init; }
    public required Guid    ReviewedBy            { get; init; }
    public string?          RejectionNotes        { get; init; }
}

public sealed record UpdatePersonAddressCommand
{
    public required Guid    PersonAddressId  { get; init; }
    public required string  AddressLine1     { get; init; } = default!;
    public string?          AddressLine2     { get; init; }
    public required string  City             { get; init; } = default!;
    public required string  StateCode        { get; init; } = default!;
    public required string  PostalCode       { get; init; } = default!;
    public required string  CountryCode      { get; init; } = default!;
    public string?          PhonePrimary     { get; init; }
    public string?          PhoneSecondary   { get; init; }
    public string?          EmailPersonal    { get; init; }
}

public sealed record ChangeManagerCommand
{
    public required Guid     EmploymentId           { get; init; }
    public required Guid     NewManagerEmploymentId { get; init; }
    public required DateOnly EffectiveDate          { get; init; }
    public required Guid     InitiatedBy            { get; init; }
    public string?           Notes                  { get; init; }
}

// ============================================================
// LEAVE COMMANDS
// ============================================================

public sealed record SubmitLeaveRequestCommand
{
    public required Guid     EmploymentId    { get; init; }
    public required string   LeaveType       { get; init; }
    public required DateOnly LeaveStartDate  { get; init; }
    public required DateOnly LeaveEndDate    { get; init; }
    public required string   LeaveReasonCode { get; init; }
    public required Guid     SubmittedBy     { get; init; }
    public string?           Notes           { get; init; }
}

public sealed record ApproveLeaveRequestCommand
{
    public required Guid  LeaveRequestId { get; init; }
    public required Guid  ApprovedBy     { get; init; }
    public string?        Notes          { get; init; }
}

public sealed record DenyLeaveRequestCommand
{
    public required Guid  LeaveRequestId { get; init; }
    public required Guid  DeniedBy       { get; init; }
    public required string DenialReason  { get; init; }
}

public sealed record ReturnFromLeaveCommand
{
    public required Guid     EmploymentId { get; init; }
    public required DateOnly ReturnDate   { get; init; }
    public required Guid     InitiatedBy  { get; init; }
}

// ============================================================
// DOCUMENT COMMANDS
// ============================================================

public sealed record UploadDocumentCommand
{
    public required Guid      PersonId      { get; init; }
    public Guid?              EmploymentId  { get; init; }
    public required string    DocumentType  { get; init; }
    public required string    DocumentName  { get; init; }
    public required Stream    FileContent   { get; init; }
    public required string    FileFormat    { get; init; }
    public required DateOnly  EffectiveDate { get; init; }
    public DateOnly?          ExpirationDate { get; init; }
    public required Guid      UploadedBy    { get; init; }
}

public sealed record VerifyDocumentCommand
{
    public required Guid DocumentId  { get; init; }
    public required Guid VerifiedBy  { get; init; }
}

public sealed record ArchiveDocumentCommand
{
    public required Guid DocumentId  { get; init; }
    public required Guid ArchivedBy  { get; init; }
}

// ============================================================
// ORG STRUCTURE COMMANDS
// ============================================================

public sealed record CreateOrgUnitCommand
{
    public required string   OrgUnitTypeCode        { get; init; }
    public required string   OrgUnitCode            { get; init; }
    public required string   OrgUnitName            { get; init; }
    public Guid?             ParentOrgUnitId        { get; init; }
    public Guid?             LegalEntityId          { get; init; }
    public int?              LegalEntityTypeId      { get; init; }
    public string?           TaxRegistrationNumber  { get; init; }
    public string?           StateOfIncorporation   { get; init; }
    public string?           CountryCode            { get; init; }
    public required DateOnly EffectiveStartDate     { get; init; }
    public required Guid     InitiatedBy            { get; init; }
}

public sealed record UpdateOrgUnitCommand
{
    public required Guid     OrgUnitId              { get; init; }
    public required string   OrgUnitCode            { get; init; }
    public required string   OrgUnitName            { get; init; }
    public int?              LegalEntityTypeId      { get; init; }
    public string?           TaxRegistrationNumber  { get; init; }
    public string?           StateOfIncorporation   { get; init; }
    public string?           CountryCode            { get; init; }
    public required Guid     UpdatedBy              { get; init; }
}

// ============================================================
// JOB AND POSITION COMMANDS
// ============================================================

public sealed record CreateJobCommand
{
    public required Guid     LegalEntityId        { get; init; }
    public required string   JobCode              { get; init; }
    public required string   JobTitle             { get; init; }
    public string?           JobFamily            { get; init; }
    public string?           JobLevel             { get; init; }
    public required string   FlsaClassificationCode { get; init; }
    public required string   EeoCategoryCode      { get; init; }
    public required DateOnly EffectiveStartDate   { get; init; }
    public required Guid     InitiatedBy          { get; init; }
}

public sealed record CreatePositionCommand
{
    public required Guid     JobId                { get; init; }
    public required Guid     OrgUnitId            { get; init; }
    public string?           PositionTitle        { get; init; }
    public int?              HeadcountBudget      { get; init; }
    public required DateOnly EffectiveStartDate   { get; init; }
    public required Guid     InitiatedBy          { get; init; }
}

public sealed record SupersedeDocumentCommand
{
    public required Guid      SupersededDocumentId { get; init; }
    public required Guid      PersonId             { get; init; }
    public Guid?              EmploymentId         { get; init; }
    public required string    DocumentType         { get; init; }
    public required string    DocumentName         { get; init; }
    public required Stream    FileContent          { get; init; }
    public required string    FileFormat           { get; init; }
    public required DateOnly  EffectiveDate        { get; init; }
    public DateOnly?          ExpirationDate       { get; init; }
    public required Guid      UploadedBy           { get; init; }
}
