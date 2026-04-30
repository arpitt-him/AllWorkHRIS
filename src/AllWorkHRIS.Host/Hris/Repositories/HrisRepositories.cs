using Dapper;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Host.Hris.Domain;
using AllWorkHRIS.Host.Hris.Queries;

namespace AllWorkHRIS.Host.Hris.Repositories;

// ============================================================
// PERSON
// ============================================================

public interface IPersonRepository
{
    Task<Person?>                      GetByIdAsync(Guid personId);
    Task<Person?>                      GetByEmploymentIdAsync(Guid employmentId);
    Task<Dictionary<Guid, string>>     GetNamesByEmploymentIdsAsync(IEnumerable<Guid> employmentIds);
    Task<Dictionary<Guid, string>>     GetNamesByPersonIdsAsync(IEnumerable<Guid> personIds);
    Task<Guid>                         InsertAsync(Person person, IUnitOfWork uow);
    Task                               UpdateAsync(Person person, IUnitOfWork uow);
    Task<IEnumerable<Person>>          SearchAsync(string searchTerm, int page, int pageSize);
}

public sealed class PersonRepository : IPersonRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public PersonRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<Person?> GetByIdAsync(Guid personId)
    {
        const string sql = "SELECT * FROM person WHERE person_id = @PersonId";
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Person>(sql, new { PersonId = personId });
    }

    public async Task<Person?> GetByEmploymentIdAsync(Guid employmentId)
    {
        const string sql = """
            SELECT p.* FROM person p
            INNER JOIN employment e ON e.person_id = p.person_id
            WHERE e.employment_id = @EmploymentId
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Person>(sql, new { EmploymentId = employmentId });
    }

    public async Task<Dictionary<Guid, string>> GetNamesByEmploymentIdsAsync(IEnumerable<Guid> employmentIds)
    {
        var ids = employmentIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, string>();

        const string sql = """
            SELECT e.employment_id, p.legal_first_name, p.legal_last_name
            FROM person p
            INNER JOIN employment e ON e.person_id = p.person_id
            WHERE e.employment_id = ANY(@Ids)
            """;
        using var conn = _connectionFactory.CreateConnection();
        var rows = await conn.QueryAsync<(Guid EmploymentId, string LegalFirstName, string LegalLastName)>(
            sql, new { Ids = ids.ToArray() });
        return rows.ToDictionary(r => r.EmploymentId, r => $"{r.LegalFirstName} {r.LegalLastName}");
    }

    public async Task<Dictionary<Guid, string>> GetNamesByPersonIdsAsync(IEnumerable<Guid> personIds)
    {
        var ids = personIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, string>();

        const string sql = """
            SELECT person_id, legal_first_name, legal_last_name
            FROM person
            WHERE person_id = ANY(@Ids)
            """;
        using var conn = _connectionFactory.CreateConnection();
        var rows = await conn.QueryAsync<(Guid PersonId, string LegalFirstName, string LegalLastName)>(
            sql, new { Ids = ids.ToArray() });
        return rows.ToDictionary(r => r.PersonId, r => $"{r.LegalFirstName} {r.LegalLastName}");
    }

    public async Task<Guid> InsertAsync(Person person, IUnitOfWork uow)
    {
        const string sql = """
            INSERT INTO person (
                person_id, person_number, legal_first_name, legal_middle_name,
                legal_last_name, name_suffix, preferred_name, date_of_birth,
                national_identifier, national_identifier_type, gender, pronouns,
                citizenship_status, work_authorization_status, work_authorization_exp_date,
                language_preference, marital_status, veteran_status, disability_status,
                person_status_id, creation_timestamp, last_update_timestamp, last_updated_by
            ) VALUES (
                @PersonId, @PersonNumber, @LegalFirstName, @LegalMiddleName,
                @LegalLastName, @NameSuffix, @PreferredName, @DateOfBirth,
                @NationalIdentifier, @NationalIdentifierType, @Gender, @Pronouns,
                @CitizenshipStatus, @WorkAuthorizationStatus, @WorkAuthorizationExpDate,
                @LanguagePreference, @MaritalStatus, @VeteranStatus, @DisabilityStatus,
                @PersonStatusId, @CreationTimestamp, @LastUpdateTimestamp, @LastUpdatedBy
            )
            """;
        await uow.Connection.ExecuteAsync(sql, person, uow.Transaction);
        return person.PersonId;
    }

    public async Task UpdateAsync(Person person, IUnitOfWork uow)
    {
        const string sql = """
            UPDATE person SET
                preferred_name        = @PreferredName,
                gender                = @Gender,
                pronouns              = @Pronouns,
                marital_status        = @MaritalStatus,
                language_preference   = @LanguagePreference,
                veteran_status        = @VeteranStatus,
                disability_status     = @DisabilityStatus,
                last_update_timestamp = @LastUpdateTimestamp,
                last_updated_by       = @LastUpdatedBy
            WHERE person_id = @PersonId
            """;
        await uow.Connection.ExecuteAsync(sql, person, uow.Transaction);
    }

    public async Task<IEnumerable<Person>> SearchAsync(string searchTerm, int page, int pageSize)
    {
        const string sql = """
            SELECT * FROM person
            WHERE legal_last_name ILIKE @Search OR legal_first_name ILIKE @Search
            ORDER BY legal_last_name, legal_first_name
            LIMIT @PageSize OFFSET @Offset
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<Person>(sql, new
        {
            Search   = $"%{searchTerm}%",
            PageSize = pageSize,
            Offset   = (page - 1) * pageSize
        });
    }
}

// ============================================================
// PERSON ADDRESS
// ============================================================

public interface IPersonAddressRepository
{
    Task<PersonAddress?> GetPrimaryAsync(Guid personId);
    Task<Guid>           InsertAsync(PersonAddress address, IUnitOfWork uow);
}

public sealed class PersonAddressRepository : IPersonAddressRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public PersonAddressRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<PersonAddress?> GetPrimaryAsync(Guid personId)
    {
        const string sql = """
            SELECT * FROM person_address
            WHERE person_id    = @PersonId
              AND address_type = 'PRIMARY'
              AND (effective_end_date IS NULL OR effective_end_date >= CURRENT_DATE)
            ORDER BY effective_start_date DESC
            LIMIT 1
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<PersonAddress>(sql, new { PersonId = personId });
    }

    public async Task<Guid> InsertAsync(PersonAddress address, IUnitOfWork uow)
    {
        const string sql = """
            INSERT INTO person_address (
                person_address_id, person_id, address_type, address_line_1, address_line_2,
                city, state_code, postal_code, country_code, phone_primary, phone_secondary,
                email_personal, effective_start_date, effective_end_date,
                created_by, creation_timestamp
            ) VALUES (
                @PersonAddressId, @PersonId, @AddressType, @AddressLine1, @AddressLine2,
                @City, @StateCode, @PostalCode, @CountryCode, @PhonePrimary, @PhoneSecondary,
                @EmailPersonal, @EffectiveStartDate, @EffectiveEndDate,
                @CreatedBy, @CreationTimestamp
            )
            """;
        await uow.Connection.ExecuteAsync(sql, address, uow.Transaction);
        return address.PersonAddressId;
    }
}

// ============================================================
// EMPLOYMENT
// ============================================================

public interface IEmploymentRepository
{
    Task<Employment?>                         GetByIdAsync(Guid employmentId);
    Task<IEnumerable<Employment>>             GetByPersonIdAsync(Guid personId);
    Task<IEnumerable<Employment>>             GetActiveByLegalEntityAsync(Guid legalEntityId, DateOnly asOf);
    Task<bool>                                ExistsWithNumberAsync(string employeeNumber);
    Task<Guid>                                InsertAsync(Employment employment, IUnitOfWork uow);
    Task                                      UpdateStatusAsync(Guid employmentId, int statusId,
                                                  DateOnly effectiveDate, IUnitOfWork uow);
    Task<PagedResult<EmploymentListItem>>     GetPagedListAsync(EmployeeListQuery query);
    Task<IReadOnlyList<EmploymentListItem>>   GetAllActiveListAsync();
    Task<EmployeeStatCards>                   GetStatCardsAsync(DateOnly asOf);
}

public sealed class EmploymentRepository : IEmploymentRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public EmploymentRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<Employment?> GetByIdAsync(Guid employmentId)
    {
        const string sql = "SELECT * FROM employment WHERE employment_id = @EmploymentId";
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Employment>(sql, new { EmploymentId = employmentId });
    }

    public async Task<IEnumerable<Employment>> GetByPersonIdAsync(Guid personId)
    {
        const string sql = """
            SELECT * FROM employment WHERE person_id = @PersonId
            ORDER BY employment_start_date DESC
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<Employment>(sql, new { PersonId = personId });
    }

    public async Task<IEnumerable<Employment>> GetActiveByLegalEntityAsync(Guid legalEntityId, DateOnly asOf)
    {
        const string sql = """
            SELECT * FROM employment
            WHERE legal_entity_id = @LegalEntityId
              AND employment_start_date <= @AsOf
              AND (employment_end_date IS NULL OR employment_end_date >= @AsOf)
              AND employment_status_id NOT IN (
                  SELECT id FROM lkp_employment_status WHERE code IN ('TERMINATED', 'CLOSED')
              )
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<Employment>(sql, new { LegalEntityId = legalEntityId, AsOf = asOf });
    }

    public async Task<bool> ExistsWithNumberAsync(string employeeNumber)
    {
        const string sql = "SELECT COUNT(1) FROM employment WHERE employee_number = @EmployeeNumber";
        using var conn = _connectionFactory.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(sql, new { EmployeeNumber = employeeNumber }) > 0;
    }

    public async Task<Guid> InsertAsync(Employment employment, IUnitOfWork uow)
    {
        const string sql = """
            INSERT INTO employment (
                employment_id, person_id, legal_entity_id, employer_id, employee_number,
                employment_type_id, employment_start_date, employment_end_date, original_hire_date,
                termination_date, employment_status_id, full_part_time_status_id,
                regular_temporary_status_id, flsa_status_id, payroll_context_id,
                primary_work_location_id, primary_department_id, manager_employment_id,
                rehire_flag, prior_employment_id, primary_flag, payroll_eligibility_flag,
                benefits_eligibility_flag, time_tracking_required_flag,
                creation_timestamp, last_update_timestamp, last_updated_by
            ) VALUES (
                @EmploymentId, @PersonId, @LegalEntityId, @EmployerId, @EmployeeNumber,
                @EmploymentTypeId, @EmploymentStartDate, @EmploymentEndDate,
                @OriginalHireDate, @TerminationDate, @EmploymentStatusId,
                @FullPartTimeStatusId, @RegularTemporaryStatusId, @FlsaStatusId,
                @PayrollContextId, @PrimaryWorkLocationId, @PrimaryDepartmentId,
                @ManagerEmploymentId, @RehireFlag, @PriorEmploymentId, @PrimaryFlag,
                @PayrollEligibilityFlag, @BenefitsEligibilityFlag, @TimeTrackingRequiredFlag,
                @CreationTimestamp, @LastUpdateTimestamp, @LastUpdatedBy
            )
            """;
        await uow.Connection.ExecuteAsync(sql, employment, uow.Transaction);
        return employment.EmploymentId;
    }

    public async Task UpdateStatusAsync(Guid employmentId, int statusId,
        DateOnly effectiveDate, IUnitOfWork uow)
    {
        const string sql = """
            UPDATE employment SET
                employment_status_id  = @StatusId,
                employment_end_date   = @EffectiveDate,
                last_update_timestamp = @Now,
                last_updated_by       = 'system'
            WHERE employment_id = @EmploymentId
            """;
        await uow.Connection.ExecuteAsync(sql, new
        {
            EmploymentId  = employmentId,
            StatusId      = statusId,
            EffectiveDate = effectiveDate,
            Now           = DateTimeOffset.UtcNow
        }, uow.Transaction);
    }

    public async Task<PagedResult<EmploymentListItem>> GetPagedListAsync(EmployeeListQuery query)
    {
        var where = new List<string>
        {
            "e.employment_status_id != (SELECT id FROM lkp_employment_status WHERE code = 'CLOSED')"
        };
        var p = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            where.Add("(p.legal_last_name ILIKE @Search OR p.legal_first_name ILIKE @Search OR e.employee_number ILIKE @Search)");
            p.Add("Search", $"%{query.SearchTerm}%");
        }
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            where.Add("e.employment_status_id = (SELECT id FROM lkp_employment_status WHERE code = @Status)");
            p.Add("Status", query.Status);
        }
        if (query.DepartmentId.HasValue)
        {
            where.Add("a.department_id = @DepartmentId");
            p.Add("DepartmentId", query.DepartmentId.Value);
        }
        if (query.StartDateFrom.HasValue)
        {
            where.Add("e.employment_start_date >= @StartDateFrom");
            p.Add("StartDateFrom", query.StartDateFrom.Value);
        }
        if (query.StartDateTo.HasValue)
        {
            where.Add("e.employment_start_date <= @StartDateTo");
            p.Add("StartDateTo", query.StartDateTo.Value);
        }

        var whereClause = "WHERE " + string.Join(" AND ", where);
        var sortColumn  = query.SortColumn ?? "p.legal_last_name";
        var sortDir     = query.SortAscending ? "ASC" : "DESC";

        var countSql = $"""
            SELECT COUNT(DISTINCT e.employment_id)
            FROM employment e
            INNER JOIN person p ON p.person_id = e.person_id
            LEFT JOIN assignment a ON a.employment_id = e.employment_id
                AND a.assignment_type_id   = (SELECT id FROM lkp_assignment_type   WHERE code = 'PRIMARY')
                AND a.assignment_status_id = (SELECT id FROM lkp_assignment_status  WHERE code = 'ACTIVE')
            LEFT JOIN org_unit d ON d.org_unit_id = a.department_id
            {whereClause}
            """;

        var dataSql = $"""
            SELECT DISTINCT ON (e.employment_id)
                e.employment_id, e.person_id, p.legal_first_name, p.legal_last_name,
                p.preferred_name, e.employee_number,
                les.code  AS employment_status,
                let2.code AS employment_type,
                e.employment_start_date, j.job_title,
                d.org_unit_name AS department_name,
                l.org_unit_name AS location_name
            FROM employment e
            INNER JOIN person p ON p.person_id = e.person_id
            LEFT JOIN lkp_employment_status les  ON les.id  = e.employment_status_id
            LEFT JOIN lkp_employment_type   let2 ON let2.id = e.employment_type_id
            LEFT JOIN assignment a ON a.employment_id = e.employment_id
                AND a.assignment_type_id   = (SELECT id FROM lkp_assignment_type   WHERE code = 'PRIMARY')
                AND a.assignment_status_id = (SELECT id FROM lkp_assignment_status  WHERE code = 'ACTIVE')
            LEFT JOIN job j      ON j.job_id         = a.job_id
            LEFT JOIN org_unit d ON d.org_unit_id    = a.department_id
            LEFT JOIN org_unit l ON l.org_unit_id    = a.location_id
            {whereClause}
            ORDER BY e.employment_id, {sortColumn} {sortDir}
            LIMIT @PageSize OFFSET @Offset
            """;

        p.Add("PageSize", query.PageSize);
        p.Add("Offset", (query.Page - 1) * query.PageSize);

        using var conn = _connectionFactory.CreateConnection();
        var total = await conn.ExecuteScalarAsync<int>(countSql, p);
        var items = await conn.QueryAsync<EmploymentListItem>(dataSql, p);

        return new PagedResult<EmploymentListItem>
        {
            Items      = items.ToList(),
            TotalCount = total,
            Page       = query.Page,
            PageSize   = query.PageSize
        };
    }

    public async Task<IReadOnlyList<EmploymentListItem>> GetAllActiveListAsync()
    {
        const string sql = """
            SELECT DISTINCT ON (e.employment_id)
                e.employment_id, e.person_id, p.legal_first_name, p.legal_last_name,
                p.preferred_name, e.employee_number,
                les.code  AS employment_status,
                let2.code AS employment_type,
                e.employment_start_date, j.job_title,
                div.org_unit_name AS division_name,  div.org_unit_id AS division_id,
                d.org_unit_name   AS department_name, d.org_unit_id   AS department_id,
                l.org_unit_name   AS location_name,   l.org_unit_id   AS location_id
            FROM employment e
            INNER JOIN person p ON p.person_id = e.person_id
            LEFT JOIN lkp_employment_status les  ON les.id  = e.employment_status_id
            LEFT JOIN lkp_employment_type   let2 ON let2.id = e.employment_type_id
            LEFT JOIN assignment a ON a.employment_id = e.employment_id
                AND a.assignment_type_id   = (SELECT id FROM lkp_assignment_type   WHERE code = 'PRIMARY')
                AND a.assignment_status_id = (SELECT id FROM lkp_assignment_status WHERE code = 'ACTIVE')
            LEFT JOIN job j      ON j.job_id           = a.job_id
            LEFT JOIN org_unit d ON d.org_unit_id      = a.department_id
            LEFT JOIN org_unit l ON l.org_unit_id      = a.location_id
            LEFT JOIN org_unit div ON div.org_unit_id  = d.parent_org_unit_id
                AND div.org_unit_type_id = (SELECT id FROM lkp_org_unit_type WHERE code = 'DIVISION')
            WHERE e.employment_status_id NOT IN (
                SELECT id FROM lkp_employment_status WHERE code IN ('TERMINATED', 'CLOSED')
            )
            ORDER BY e.employment_id, p.legal_last_name, p.legal_first_name
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<EmploymentListItem>(sql)).ToList();
    }

    public async Task<EmployeeStatCards> GetStatCardsAsync(DateOnly asOf)
    {
        const string sql = """
            SELECT
                COUNT(*) FILTER (WHERE employment_status_id = (SELECT id FROM lkp_employment_status WHERE code = 'ACTIVE'))    AS active,
                COUNT(*) FILTER (WHERE employment_status_id = (SELECT id FROM lkp_employment_status WHERE code = 'ON_LEAVE'))  AS on_leave,
                COUNT(*) FILTER (WHERE employment_type_id   = (SELECT id FROM lkp_employment_type   WHERE code = 'CONTRACTOR')) AS contractors,
                (SELECT COUNT(DISTINCT a.department_id)
                 FROM assignment a
                 WHERE a.assignment_status_id = (SELECT id FROM lkp_assignment_status WHERE code = 'ACTIVE')) AS departments
            FROM employment
            WHERE employment_start_date <= @AsOf
              AND employment_status_id != (SELECT id FROM lkp_employment_status WHERE code = 'CLOSED')
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstAsync<EmployeeStatCards>(sql,
            new { AsOf = asOf.ToDateTime(TimeOnly.MinValue) });
    }
}

// ============================================================
// ASSIGNMENT
// ============================================================

public interface IAssignmentRepository
{
    Task<Assignment?>             GetActiveByEmploymentIdAsync(Guid employmentId);
    Task<IEnumerable<Assignment>> GetAllByEmploymentIdAsync(Guid employmentId);
    Task<Guid>                    InsertAsync(Assignment assignment, IUnitOfWork uow);
    Task                          CloseAsync(Guid assignmentId, DateOnly endDate, IUnitOfWork uow);
}

public sealed class AssignmentRepository : IAssignmentRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public AssignmentRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<Assignment?> GetActiveByEmploymentIdAsync(Guid employmentId)
    {
        const string sql = """
            SELECT * FROM assignment
            WHERE employment_id      = @EmploymentId
              AND assignment_type_id   = (SELECT id FROM lkp_assignment_type   WHERE code = 'PRIMARY')
              AND assignment_status_id = (SELECT id FROM lkp_assignment_status  WHERE code = 'ACTIVE')
            LIMIT 1
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Assignment>(sql, new { EmploymentId = employmentId });
    }

    public async Task<IEnumerable<Assignment>> GetAllByEmploymentIdAsync(Guid employmentId)
    {
        const string sql = """
            SELECT * FROM assignment WHERE employment_id = @EmploymentId
            ORDER BY assignment_start_date DESC
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<Assignment>(sql, new { EmploymentId = employmentId });
    }

    public async Task<Guid> InsertAsync(Assignment assignment, IUnitOfWork uow)
    {
        const string sql = """
            INSERT INTO assignment (
                assignment_id, employment_id, job_id, position_id, department_id,
                location_id, payroll_context_id, plan_id, assignment_type_id, assignment_status_id,
                assignment_priority, assignment_start_date, assignment_end_date,
                created_by, creation_timestamp, last_updated_by, last_update_timestamp
            ) VALUES (
                @AssignmentId, @EmploymentId, @JobId, @PositionId, @DepartmentId,
                @LocationId, @PayrollContextId, @PlanId,
                @AssignmentTypeId, @AssignmentStatusId,
                @AssignmentPriority, @AssignmentStartDate, @AssignmentEndDate,
                @CreatedBy, @CreationTimestamp, @LastUpdatedBy, @LastUpdateTimestamp
            )
            """;
        await uow.Connection.ExecuteAsync(sql, assignment, uow.Transaction);
        return assignment.AssignmentId;
    }

    public async Task CloseAsync(Guid assignmentId, DateOnly endDate, IUnitOfWork uow)
    {
        const string sql = """
            UPDATE assignment SET
                assignment_status_id  = (SELECT id FROM lkp_assignment_status WHERE code = 'ENDED'),
                assignment_end_date   = @EndDate,
                last_update_timestamp = @Now,
                last_updated_by       = @UpdatedBy
            WHERE assignment_id = @AssignmentId
            """;
        await uow.Connection.ExecuteAsync(sql, new
        {
            AssignmentId = assignmentId,
            EndDate      = endDate,
            Now          = DateTimeOffset.UtcNow,
            UpdatedBy    = Guid.Empty
        }, uow.Transaction);
    }
}

// ============================================================
// COMPENSATION
// ============================================================

public interface ICompensationRepository
{
    Task<CompensationRecord?>             GetActiveByEmploymentIdAsync(Guid employmentId, DateOnly asOf);
    Task<IEnumerable<CompensationRecord>> GetHistoryByEmploymentIdAsync(Guid employmentId);
    Task<Guid>                            InsertAsync(CompensationRecord record, IUnitOfWork uow);
    Task                                  CloseCurrentAsync(Guid employmentId, DateOnly endDate, IUnitOfWork uow);
}

public sealed class CompensationRepository : ICompensationRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public CompensationRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<CompensationRecord?> GetActiveByEmploymentIdAsync(Guid employmentId, DateOnly asOf)
    {
        const string sql = """
            SELECT * FROM compensation_record
            WHERE employment_id          = @EmploymentId
              AND primary_rate_flag       = true
              AND compensation_status_id  = (SELECT id FROM lkp_compensation_status WHERE code = 'ACTIVE')
              AND effective_start_date   <= @AsOf
              AND (effective_end_date IS NULL OR effective_end_date >= @AsOf)
            LIMIT 1
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<CompensationRecord>(sql,
            new { EmploymentId = employmentId, AsOf = asOf });
    }

    public async Task<IEnumerable<CompensationRecord>> GetHistoryByEmploymentIdAsync(Guid employmentId)
    {
        const string sql = """
            SELECT * FROM compensation_record
            WHERE employment_id = @EmploymentId
            ORDER BY effective_start_date DESC
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<CompensationRecord>(sql, new { EmploymentId = employmentId });
    }

    public async Task<Guid> InsertAsync(CompensationRecord record, IUnitOfWork uow)
    {
        const string sql = """
            INSERT INTO compensation_record (
                compensation_id, employment_id, rate_type_id, base_rate, rate_currency,
                annual_equivalent, pay_frequency_id, effective_start_date, effective_end_date,
                compensation_status_id, change_reason_code, approval_status_id, approved_by,
                approval_timestamp, primary_rate_flag, created_by, creation_timestamp,
                last_updated_by, last_update_timestamp
            ) VALUES (
                @CompensationId, @EmploymentId, @RateTypeId, @BaseRate, @RateCurrency,
                @AnnualEquivalent, @PayFrequencyId, @EffectiveStartDate, @EffectiveEndDate,
                @CompensationStatusId, @ChangeReasonCode, @ApprovalStatusId, @ApprovedBy,
                @ApprovalTimestamp, @PrimaryRateFlag, @CreatedBy, @CreationTimestamp,
                @LastUpdatedBy, @LastUpdateTimestamp
            )
            """;
        await uow.Connection.ExecuteAsync(sql, record, uow.Transaction);
        return record.CompensationId;
    }

    public async Task CloseCurrentAsync(Guid employmentId, DateOnly endDate, IUnitOfWork uow)
    {
        const string sql = """
            UPDATE compensation_record SET
                compensation_status_id = (SELECT id FROM lkp_compensation_status WHERE code = 'ENDED'),
                effective_end_date     = @EndDate,
                last_update_timestamp  = @Now,
                last_updated_by        = @UpdatedBy
            WHERE employment_id         = @EmploymentId
              AND primary_rate_flag      = true
              AND compensation_status_id = (SELECT id FROM lkp_compensation_status WHERE code = 'ACTIVE')
            """;
        await uow.Connection.ExecuteAsync(sql, new
        {
            EmploymentId = employmentId,
            EndDate      = endDate,
            Now          = DateTimeOffset.UtcNow,
            UpdatedBy    = Guid.Empty
        }, uow.Transaction);
    }
}

// ============================================================
// ORG UNIT
// ============================================================

public interface IOrgUnitRepository
{
    Task<OrgUnit?>                  GetByIdAsync(Guid orgUnitId);
    Task<IEnumerable<OrgUnit>>      GetByTypeAsync(int orgUnitTypeId);
    Task<IEnumerable<OrgUnit>>      GetChildrenAsync(Guid parentOrgUnitId);
    Task<IEnumerable<OrgUnit>>      GetAllActiveAsync(Guid? legalEntityId = null);
    Task<IEnumerable<OrgUnitEmployee>> GetWorkforceByOrgUnitAsync(Guid orgUnitId);
    Task<Guid>                      InsertAsync(OrgUnit orgUnit, IUnitOfWork uow);
}

public sealed class OrgUnitRepository : IOrgUnitRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public OrgUnitRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<OrgUnit?> GetByIdAsync(Guid orgUnitId)
    {
        const string sql = "SELECT * FROM org_unit WHERE org_unit_id = @OrgUnitId";
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<OrgUnit>(sql, new { OrgUnitId = orgUnitId });
    }

    public async Task<IEnumerable<OrgUnit>> GetByTypeAsync(int orgUnitTypeId)
    {
        const string sql = """
            SELECT * FROM org_unit
            WHERE org_unit_type_id = @OrgUnitTypeId
              AND org_status_id    = (SELECT id FROM lkp_org_status WHERE code = 'ACTIVE')
            ORDER BY org_unit_name
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<OrgUnit>(sql, new { OrgUnitTypeId = orgUnitTypeId });
    }

    public async Task<IEnumerable<OrgUnit>> GetChildrenAsync(Guid parentOrgUnitId)
    {
        const string sql = """
            SELECT * FROM org_unit
            WHERE parent_org_unit_id = @ParentId
              AND org_status_id      = (SELECT id FROM lkp_org_status WHERE code = 'ACTIVE')
            ORDER BY org_unit_name
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<OrgUnit>(sql, new { ParentId = parentOrgUnitId });
    }

    public async Task<IEnumerable<OrgUnit>> GetAllActiveAsync(Guid? legalEntityId = null)
    {
        using var conn = _connectionFactory.CreateConnection();

        if (legalEntityId.HasValue)
        {
            const string sql = """
                SELECT * FROM org_unit
                WHERE org_status_id    = (SELECT id FROM lkp_org_status WHERE code = 'ACTIVE')
                  AND legal_entity_id  = @LegalEntityId
                ORDER BY org_unit_name
                """;
            return await conn.QueryAsync<OrgUnit>(sql, new { LegalEntityId = legalEntityId.Value });
        }
        else
        {
            const string sql = """
                SELECT * FROM org_unit
                WHERE org_status_id = (SELECT id FROM lkp_org_status WHERE code = 'ACTIVE')
                ORDER BY org_unit_name
                """;
            return await conn.QueryAsync<OrgUnit>(sql);
        }
    }

    public async Task<IEnumerable<OrgUnitEmployee>> GetWorkforceByOrgUnitAsync(Guid orgUnitId)
    {
        const string sql = """
            WITH RECURSIVE org_tree AS (
                SELECT org_unit_id FROM org_unit WHERE org_unit_id = @OrgUnitId
                UNION ALL
                SELECT o.org_unit_id FROM org_unit o
                INNER JOIN org_tree t ON o.parent_org_unit_id = t.org_unit_id
            )
            SELECT
                e.employment_id,
                e.person_id,
                e.employee_number,
                p.legal_first_name,
                p.legal_last_name,
                p.preferred_name,
                COALESCE(j.job_title, '')           AS job_title,
                COALESCE(lfp.code,    'UNKNOWN')    AS full_part_time,
                COALESCE(lfs.code,    'UNKNOWN')    AS flsa_status,
                COALESCE(cr.annual_equivalent, 0)   AS annual_equivalent,
                COALESCE(lcrt.code,   '')            AS rate_type,
                COALESCE(lpf.code,    '')            AS pay_frequency,
                ou.org_unit_name                    AS department_name,
                e.employment_start_date
            FROM employment e
            INNER JOIN person p      ON p.person_id      = e.person_id
            INNER JOIN assignment a  ON a.employment_id  = e.employment_id
                AND a.assignment_type_id   = (SELECT id FROM lkp_assignment_type   WHERE code = 'PRIMARY')
                AND a.assignment_status_id = (SELECT id FROM lkp_assignment_status WHERE code = 'ACTIVE')
            INNER JOIN org_tree ot   ON a.department_id  = ot.org_unit_id
            INNER JOIN org_unit ou   ON ou.org_unit_id   = a.department_id
            LEFT  JOIN job j         ON j.job_id         = a.job_id
            LEFT  JOIN compensation_record cr ON cr.employment_id = e.employment_id
                AND cr.compensation_status_id = (SELECT id FROM lkp_compensation_status WHERE code = 'ACTIVE')
                AND cr.primary_rate_flag = true
            LEFT  JOIN lkp_full_part_time_status lfp  ON lfp.id  = e.full_part_time_status_id
            LEFT  JOIN lkp_flsa_status           lfs  ON lfs.id  = e.flsa_status_id
            LEFT  JOIN lkp_compensation_rate_type lcrt ON lcrt.id = cr.rate_type_id
            LEFT  JOIN lkp_pay_frequency          lpf  ON lpf.id  = cr.pay_frequency_id
            WHERE e.employment_status_id = (SELECT id FROM lkp_employment_status WHERE code = 'ACTIVE')
            ORDER BY p.legal_last_name, p.legal_first_name
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<OrgUnitEmployee>(sql, new { OrgUnitId = orgUnitId });
    }

    public async Task<Guid> InsertAsync(OrgUnit orgUnit, IUnitOfWork uow)
    {
        const string sql = """
            INSERT INTO org_unit (
                org_unit_id, org_unit_type_id, org_unit_code, org_unit_name, parent_org_unit_id,
                legal_entity_id, org_status_id, effective_start_date, effective_end_date,
                tax_registration_number, country_code, state_of_incorporation, legal_entity_type,
                address_line_1, address_line_2, city, state_code, postal_code, locality_code,
                work_location_type_id,
                created_by, creation_timestamp, last_updated_by, last_update_timestamp
            ) VALUES (
                @OrgUnitId, @OrgUnitTypeId, @OrgUnitCode, @OrgUnitName, @ParentOrgUnitId,
                @LegalEntityId, @OrgStatusId, @EffectiveStartDate, @EffectiveEndDate,
                @TaxRegistrationNumber, @CountryCode, @StateOfIncorporation, @LegalEntityType,
                @AddressLine1, @AddressLine2, @City, @StateCode, @PostalCode, @LocalityCode,
                @WorkLocationTypeId,
                @CreatedBy, @CreationTimestamp, @LastUpdatedBy, @LastUpdateTimestamp
            )
            """;
        await uow.Connection.ExecuteAsync(sql, orgUnit, uow.Transaction);
        return orgUnit.OrgUnitId;
    }
}

// ============================================================
// JOB
// ============================================================

public interface IJobRepository
{
    Task<Job?>             GetByIdAsync(Guid jobId);
    Task<IEnumerable<Job>> GetAllActiveAsync();
    Task<Guid>             InsertAsync(Job job, IUnitOfWork uow);
}

public sealed class JobRepository : IJobRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public JobRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<Job?> GetByIdAsync(Guid jobId)
    {
        const string sql = "SELECT * FROM job WHERE job_id = @JobId";
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Job>(sql, new { JobId = jobId });
    }

    public async Task<IEnumerable<Job>> GetAllActiveAsync()
    {
        const string sql = """
            SELECT * FROM job
            WHERE job_status_id = (SELECT id FROM lkp_job_status WHERE code = 'ACTIVE')
            ORDER BY job_title
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<Job>(sql);
    }

    public async Task<Guid> InsertAsync(Job job, IUnitOfWork uow)
    {
        const string sql = """
            INSERT INTO job (
                job_id, job_code, job_title, job_family, job_level,
                flsa_classification_id, eeo_category_id, job_status_id,
                effective_start_date, effective_end_date,
                created_by, creation_timestamp, last_updated_by, last_update_timestamp
            ) VALUES (
                @JobId, @JobCode, @JobTitle, @JobFamily, @JobLevel,
                @FlsaClassificationId, @EeoCategoryId, @JobStatusId,
                @EffectiveStartDate, @EffectiveEndDate,
                @CreatedBy, @CreationTimestamp, @LastUpdatedBy, @LastUpdateTimestamp
            )
            """;
        await uow.Connection.ExecuteAsync(sql, job, uow.Transaction);
        return job.JobId;
    }
}

// ============================================================
// POSITION
// ============================================================

public interface IPositionRepository
{
    Task<Position?>             GetByIdAsync(Guid positionId);
    Task<IEnumerable<Position>> GetByJobIdAsync(Guid jobId);
    Task<IEnumerable<Position>> GetAllActiveAsync();
    Task<Guid>                  InsertAsync(Position position, IUnitOfWork uow);
}

public sealed class PositionRepository : IPositionRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public PositionRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<Position?> GetByIdAsync(Guid positionId)
    {
        const string sql = "SELECT * FROM position WHERE position_id = @PositionId";
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Position>(sql, new { PositionId = positionId });
    }

    public async Task<IEnumerable<Position>> GetByJobIdAsync(Guid jobId)
    {
        const string sql = """
            SELECT * FROM position
            WHERE job_id = @JobId
              AND position_status_id NOT IN (
                  SELECT id FROM lkp_position_status WHERE code = 'ABOLISHED'
              )
            ORDER BY position_status_id
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<Position>(sql, new { JobId = jobId });
    }

    public async Task<IEnumerable<Position>> GetAllActiveAsync()
    {
        const string sql = """
            SELECT * FROM position
            WHERE position_status_id NOT IN (
                SELECT id FROM lkp_position_status WHERE code = 'ABOLISHED'
            )
            ORDER BY effective_start_date DESC
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<Position>(sql);
    }

    public async Task<Guid> InsertAsync(Position position, IUnitOfWork uow)
    {
        const string sql = """
            INSERT INTO position (
                position_id, job_id, org_unit_id, position_title, headcount_budget,
                position_status_id, effective_start_date, effective_end_date,
                created_by, creation_timestamp, last_updated_by, last_update_timestamp
            ) VALUES (
                @PositionId, @JobId, @OrgUnitId, @PositionTitle, @HeadcountBudget,
                @PositionStatusId, @EffectiveStartDate, @EffectiveEndDate,
                @CreatedBy, @CreationTimestamp, @LastUpdatedBy, @LastUpdateTimestamp
            )
            """;
        await uow.Connection.ExecuteAsync(sql, position, uow.Transaction);
        return position.PositionId;
    }
}

// ============================================================
// EMPLOYEE EVENT
// ============================================================

public interface IEmployeeEventRepository
{
    Task<EmployeeEvent?>             GetByIdAsync(Guid eventId);
    Task<IEnumerable<EmployeeEvent>> GetByEmploymentIdAsync(Guid employmentId);
    Task<Guid>                       InsertAsync(EmployeeEvent employeeEvent, IUnitOfWork uow);
}

public sealed class EmployeeEventRepository : IEmployeeEventRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public EmployeeEventRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<EmployeeEvent?> GetByIdAsync(Guid eventId)
    {
        const string sql = "SELECT * FROM employee_event WHERE event_id = @EventId";
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<EmployeeEvent>(sql, new { EventId = eventId });
    }

    public async Task<IEnumerable<EmployeeEvent>> GetByEmploymentIdAsync(Guid employmentId)
    {
        const string sql = """
            SELECT * FROM employee_event
            WHERE employment_id = @EmploymentId
            ORDER BY effective_date DESC, creation_timestamp DESC
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<EmployeeEvent>(sql, new { EmploymentId = employmentId });
    }

    public async Task<Guid> InsertAsync(EmployeeEvent employeeEvent, IUnitOfWork uow)
    {
        const string sql = """
            INSERT INTO employee_event (
                event_id, employment_id, event_type_id, effective_date,
                event_reason, notes, initiated_by, approved_by,
                approval_timestamp, creation_timestamp
            ) VALUES (
                @EventId, @EmploymentId, @EventTypeId, @EffectiveDate,
                @EventReason, @Notes, @InitiatedBy, @ApprovedBy,
                @ApprovalTimestamp, @CreationTimestamp
            )
            """;
        await uow.Connection.ExecuteAsync(sql, employeeEvent, uow.Transaction);
        return employeeEvent.EventId;
    }
}
