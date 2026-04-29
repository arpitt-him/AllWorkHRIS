using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Host.Hris.Commands;

namespace AllWorkHRIS.Host.Hris.Domain;

public sealed record Person
{
    public Guid PersonId { get; init; }
    public string? PersonNumber { get; init; }
    public string LegalFirstName { get; init; } = default!;
    public string? LegalMiddleName { get; init; }
    public string LegalLastName { get; init; } = default!;
    public string? NameSuffix { get; init; }
    public string? PreferredName { get; init; }
    public DateOnly DateOfBirth { get; init; }
    public string? NationalIdentifier { get; init; }
    public string? NationalIdentifierType { get; init; }
    public string? Gender { get; init; }
    public string? Pronouns { get; init; }
    public string? CitizenshipStatus { get; init; }
    public string? WorkAuthorizationStatus { get; init; }
    public DateOnly? WorkAuthorizationExpDate { get; init; }
    public string? LanguagePreference { get; init; }
    public string? MaritalStatus { get; init; }
    public string? VeteranStatus { get; init; }
    public string? DisabilityStatus { get; init; }
    public int PersonStatusId { get; init; }
    public DateTimeOffset CreationTimestamp { get; init; }
    public DateTimeOffset LastUpdateTimestamp { get; init; }
    public string LastUpdatedBy { get; init; } = default!;

    public static Person CreateNew(HireEmployeeCommand command, ILookupCache lookupCache)
    {
        var now = DateTimeOffset.UtcNow;
        return new Person
        {
            PersonId               = Guid.NewGuid(),
            LegalFirstName         = command.LegalFirstName,
            LegalMiddleName        = command.LegalMiddleName,
            LegalLastName          = command.LegalLastName,
            PreferredName          = command.PreferredName,
            DateOfBirth            = command.DateOfBirth,
            NationalIdentifier     = command.NationalIdentifier,
            NationalIdentifierType = "SSN",
            PersonStatusId         = lookupCache.GetId(LookupTables.PersonStatus, "ACTIVE"),
            CreationTimestamp      = now,
            LastUpdateTimestamp    = now,
            LastUpdatedBy          = command.InitiatedBy.ToString()
        };
    }
}

public sealed record PersonAddress
{
    public Guid PersonAddressId { get; init; }
    public Guid PersonId { get; init; }
    public string AddressType { get; init; } = default!;
    public string AddressLine1 { get; init; } = default!;
    public string? AddressLine2 { get; init; }
    public string City { get; init; } = default!;
    public string StateCode { get; init; } = default!;
    public string PostalCode { get; init; } = default!;
    public string CountryCode { get; init; } = default!;
    public string? PhonePrimary { get; init; }
    public string? PhoneSecondary { get; init; }
    public string? EmailPersonal { get; init; }
    public DateOnly EffectiveStartDate { get; init; }
    public DateOnly? EffectiveEndDate { get; init; }
    public Guid CreatedBy { get; init; }
    public DateTimeOffset CreationTimestamp { get; init; }

    public static PersonAddress CreateFromHire(HireEmployeeCommand command, Guid personId)
        => new()
        {
            PersonAddressId    = Guid.NewGuid(),
            PersonId           = personId,
            AddressType        = "PRIMARY",
            AddressLine1       = command.AddressLine1,
            AddressLine2       = command.AddressLine2,
            City               = command.City,
            StateCode          = command.StateCode,
            PostalCode         = command.PostalCode,
            CountryCode        = command.CountryCode,
            PhonePrimary       = command.PhonePrimary,
            EmailPersonal      = command.EmailPersonal,
            EffectiveStartDate = command.EmploymentStartDate,
            CreatedBy          = command.InitiatedBy,
            CreationTimestamp  = DateTimeOffset.UtcNow
        };
}

public sealed record PersonEmergencyContact
{
    public Guid EmergencyContactId { get; init; }
    public Guid PersonId { get; init; }
    public string ContactName { get; init; } = default!;
    public string? Relationship { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public bool PrimaryFlag { get; init; }
    public Guid CreatedBy { get; init; }
    public DateTimeOffset CreationTimestamp { get; init; }
}
