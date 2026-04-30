namespace AllWorkHRIS.Host.Hris.Queries;

public sealed record PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}

public sealed record EmployeeListQuery
{
    public int       Page          { get; init; } = 1;
    public int       PageSize      { get; init; } = 20;
    public string?   SearchTerm    { get; init; }
    public string?   Status        { get; init; }
    public Guid?     DepartmentId  { get; init; }
    public DateOnly? StartDateFrom { get; init; }
    public DateOnly? StartDateTo   { get; init; }
    public string?   SortColumn    { get; init; } = "legal_last_name";
    public bool      SortAscending { get; init; } = true;
}

public sealed record EmploymentListItem
{
    public Guid     EmploymentId        { get; init; }
    public Guid     PersonId            { get; init; }
    public string   LegalFirstName      { get; init; } = default!;
    public string   LegalLastName       { get; init; } = default!;
    public string?  PreferredName       { get; init; }
    public string   EmployeeNumber      { get; init; } = default!;
    public string   EmploymentStatus    { get; init; } = default!;
    public string   EmploymentType      { get; init; } = default!;
    public DateOnly EmploymentStartDate { get; init; }
    public string?  JobTitle            { get; init; }
    public string?  DivisionName        { get; init; }
    public Guid?    DivisionId          { get; init; }
    public string?  DepartmentName      { get; init; }
    public Guid?    DepartmentId        { get; init; }
    public string?  LocationName        { get; init; }
    public Guid?    LocationId          { get; init; }
}

public sealed record HireResult
{
    public Guid PersonId     { get; init; }
    public Guid EmploymentId { get; init; }
    public Guid EventId      { get; init; }

    public HireResult(Guid personId, Guid employmentId, Guid eventId)
    {
        PersonId     = personId;
        EmploymentId = employmentId;
        EventId      = eventId;
    }
}

public sealed record EmployeeStatCards
{
    public int Active       { get; init; }
    public int OnLeave      { get; init; }
    public int Contractors  { get; init; }
    public int Departments  { get; init; }
}

public sealed record OrgUnitEmployee
{
    public Guid     EmploymentId        { get; init; }
    public Guid     PersonId            { get; init; }
    public string   EmployeeNumber      { get; init; } = default!;
    public string   LegalFirstName      { get; init; } = default!;
    public string   LegalLastName       { get; init; } = default!;
    public string?  PreferredName       { get; init; }
    public string   JobTitle            { get; init; } = default!;
    public string   FullPartTime        { get; init; } = default!;
    public string   FlsaStatus          { get; init; } = default!;
    public decimal  AnnualEquivalent    { get; init; }
    public string   RateType            { get; init; } = default!;
    public string   PayFrequency        { get; init; } = default!;
    public string   DepartmentName      { get; init; } = default!;
    public DateOnly EmploymentStartDate { get; init; }
}
