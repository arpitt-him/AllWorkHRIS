namespace AllWorkHRIS.Host.Hris.Domain;

public sealed record HrDocument
{
    public Guid      DocumentId              { get; init; }
    public Guid      PersonId               { get; init; }
    public Guid?     EmploymentId           { get; init; }
    public int       DocumentTypeId         { get; init; }
    public string    DocumentName           { get; init; } = default!;
    public int       DocumentVersion        { get; init; }
    public int       DocumentStatusId       { get; init; }
    public DateOnly  EffectiveDate          { get; init; }
    public DateOnly? ExpirationDate         { get; init; }
    public string    StorageReference       { get; init; } = default!;
    public string    FileFormat             { get; init; } = default!;
    public DateTimeOffset UploadDate        { get; init; }
    public Guid      UploadedBy             { get; init; }
    public Guid?     VerifiedBy             { get; init; }
    public DateTimeOffset? VerificationDate { get; init; }
    public Guid?     SupersededByDocumentId { get; init; }
    public bool      LegalHoldFlag          { get; init; }
    public Guid      CreatedBy              { get; init; }
    public DateTimeOffset CreationTimestamp { get; init; }
}

public sealed record DocumentStorageOptions
{
    public string BasePath      { get; init; } = "App_Data/documents";
    public int    MaxFileSizeMB { get; init; } = 25;
}
