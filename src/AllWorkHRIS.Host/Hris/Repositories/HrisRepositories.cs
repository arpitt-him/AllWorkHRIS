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
    Task<Person?>             GetByIdAsync(Guid personId);
    Task<Person?>             GetByEmploymentIdAsync(Guid employmentId);
    Task<Guid>                InsertAsync(Person person, IUnitOfWork uow);
    Task                      UpdateAsync(Person person, IUnitOfWork uow);
    Task<IEnumerable<Person>> SearchAsync(string searchTerm, int page, int pageSize);
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

    public async Task<Guid> InsertAsync(Person person, IUnitOfWork uow)
    {
        const string sql = """
            INSERT INTO person (
                person_id, person_number, legal_first_name, legal_middle_name,
                legal_last_name, name_suffix, preferred_name, date_of_birth,
                national_identifier, national_identifier_type, gender, pronouns,
                citizenship_status, work_authorization_status, work_authorization_exp_date,
                language_preference, marital_status, veteran_status, disability_status,
                person_status, creation_timestamp, last_update_timestamp, last_updated_by
            ) VALUES (
                @PersonId, @PersonNumber, @LegalFirstName, @LegalMiddleName,
                @LegalLastName, @NameSuffix, @PreferredName, @DateOfBirth,
                @NationalIdentifier, @NationalIdentifierType, @Gender, @Pronouns,
                @CitizenshipStatus, @WorkAuthorizationStatus, @WorkAuthorizationExpDate,
                @LanguagePreference, @MaritalStatus, @VeteranStatus, @DisabilityStatus,
                @PersonStatus::person_status, @CreationTimestamp, @LastUpdateTimestamp, @LastUpdatedBy
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
// EMPLOYMENT
// ============================================================

public interface IEmploymentRepository
{
    Task<Employment?>                         GetByIdAsync(Guid employmentId);
    Task<IEnumerable<Employment>>             GetByPersonIdAsync(Guid personId);
    Task<IEnumerable<Employment>>             GetActiveByLegalEntityAsync(Guid legalEntityId, DateOnly asOf);
    Task<bool>                                ExistsWithNumberAsync(string employeeNumber);
    Task<Guid>                                InsertAsync(Employment employment, IUnitOfWork uow);
    Task                                      UpdateStatusAsync(Guid employmentId, string status,
                                                  DateOnly effectiveDate, IUnitOfWork uow);
    Task<PagedResult<EmploymentListItem>>     GetPagedListAsync(EmployeeListQuery query);
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
              AND employment_status NOT IN ('TERMINATED', 'CLOSED')
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
                employment_type, employment_start_date, employment_end_date, original_hire_date,
                termination_date, employment_status, full_or_part_time_status,
                regular_or_temporary_status, flsa_status, payroll_context_id,
                primary_work_location_id, primary_department_id, manager_employment_id,
                rehire_flag, prior_employment_id, primary_flag, payroll_eligibility_flag,
                benefits_eligibility_flag, time_tracking_required_flag,
                creation_timestamp, last_update_timestamp, last_updated_by
            ) VALUES (
                @EmploymentId, @PersonId, @LegalEntityId, @EmployerId, @EmployeeNumber,
                @EmploymentType::employment_type, @EmploymentStartDate, @EmploymentEndDate,
                @OriginalHireDate, @TerminationDate, @EmploymentStatus::employment_status,
                @FullOrPartTimeStatus::full_part_time_status,
                @RegularOrTemporaryStatus::regular_temporary_status,
                @FlsaStatus::flsa_status, @PayrollContextId,
                @PrimaryWorkLocationId, @PrimaryDepartmentId, @ManagerEmploymentId,
                @RehireFlag, @PriorEmploymentId, @PrimaryFlag, @PayrollEligibilityFlag,
                @BenefitsEligibilityFlag, @TimeTrackingRequiredFlag,
                @CreationTimestamp, @LastUpdateTimestamp, @LastUpdatedBy
            )
            """;
        await uow.Connection.ExecuteAsync(sql, employment, uow.Transaction);
        return employment.EmploymentId;
    }

    public async Task UpdateStatusAsync(Guid employmentId, string status,
        DateOnly effectiveDate, IUnitOfWork uow)
    {
        const string sql = """
            UPDATE employment SET
                employment_status     = @Status::employment_status,
                employment_end_date   = @EffectiveDate,
                last_update_timestamp = @Now,
                last_updated_by       = 'system'
            WHERE employment_id = @EmploymentId
            """;
        await uow.Connection.ExecuteAsync(sql, new
        {
            EmploymentId  = employmentId,
            Status        = status,
            EffectiveDate = effectiveDate,
            Now           = DateTimeOffset.UtcNow
        }, uow.Transaction);
    }

    public async Task<PagedResult<EmploymentListItem>> GetPagedListAsync(EmployeeListQuery query)
    {
        var where = new List<string> { "e.employment_status != 'CLOSED'" };
        var p = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            where.Add("(p.legal_last_name ILIKE @Search OR p.legal_first_name ILIKE @Search OR e.employee_number ILIKE @Search)");
            p.Add("Search", $"%{query.SearchTerm}%");
        }
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            where.Add("e.employment_status = @Status::employment_status");
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
                AND a.assignment_type = 'PRIMARY' AND a.assignment_status = 'ACTIVE'
            LEFT JOIN org_unit d ON d.org_unit_id = a.department_id
            {whereClause}
            """;

        var dataSql = $"""
            SELECT DISTINCT ON (e.employment_id)
                e.employment_id, e.person_id, p.legal_first_name, p.legal_last_name,
                p.preferred_name, e.employee_number, e.employment_status::text, e.employment_type::text,
                e.employment_start_date, j.job_title, d.org_unit_name AS department_name,
                c.base_rate, c.rate_type::text, c.pay_frequency::text
            FROM employment e
            INNER JOIN person p ON p.person_id = e.person_id
            LEFT JOIN assignment a ON a.employment_id = e.employment_id
                AND a.assignment_type = 'PRIMARY' AND a.assignment_status = 'ACTIVE'
            LEFT JOIN job j ON j.job_id = a.job_id
            LEFT JOIN org_unit d ON d.org_unit_id = a.department_id
            LEFT JOIN compensation_record c ON c.employment_id = e.employment_id
                AND c.compensation_status = 'ACTIVE' AND c.primary_rate_flag = true
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

    public async Task<EmployeeStatCards> GetStatCardsAsync(DateOnly asOf)
    {
        const string sql = """
            SELECT
                COUNT(*) FILTER (WHERE employment_status = 'ACTIVE')    AS active,
                COUNT(*) FILTER (WHERE employment_status = 'ON_LEAVE')  AS on_leave,
                COUNT(*) FILTER (WHERE employment_type  = 'CONTRACTOR') AS contractors,
                (SELECT COUNT(DISTINCT a.department_id)
                 FROM assignment a
                 WHERE a.assignment_status = 'ACTIVE')                  AS departments
            FROM employment
            WHERE employment_start_date <= @AsOf
              AND employment_status NOT IN ('CLOSED')
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstAsync<EmployeeStatCards>(sql, new { AsOf = asOf });
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
            WHERE employment_id    = @EmploymentId
              AND assignment_type   = 'PRIMARY'
              AND assignment_status = 'ACTIVE'
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
                location_id, payroll_context_id, plan_id, assignment_type, assignment_status,
                assignment_priority, assignment_start_date, assignment_end_date,
                created_by, creation_timestamp, last_updated_by, last_update_timestamp
            ) VALUES (
                @AssignmentId, @EmploymentId, @JobId, @PositionId, @DepartmentId,
                @LocationId, @PayrollContextId, @PlanId,
                @AssignmentType::assignment_type, @AssignmentStatus::assignment_status,
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
                assignment_status   = 'CLOSED'::assignment_status,
                assignment_end_date = @EndDate,
                last_update_timestamp = @Now,
                last_updated_by     = @UpdatedBy
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
            WHERE employment_id     = @EmploymentId
              AND primary_rate_flag = true
              AND compensation_status = 'ACTIVE'
              AND effective_start_date <= @AsOf
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
                compensation_id, employment_id, rate_type, base_rate, rate_currency,
                annual_equivalent, pay_frequency, effective_start_date, effective_end_date,
                compensation_status, change_reason_code, approval_status, approved_by,
                approval_timestamp, primary_rate_flag, created_by, creation_timestamp,
                last_updated_by, last_update_timestamp
            ) VALUES (
                @CompensationId, @EmploymentId, @RateType::compensation_rate_type,
                @BaseRate, @RateCurrency, @AnnualEquivalent, @PayFrequency::pay_frequency,
                @EffectiveStartDate, @EffectiveEndDate, @CompensationStatus::compensation_status,
                @ChangeReasonCode, @ApprovalStatus::approval_status, @ApprovedBy,
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
                compensation_status   = 'SUPERSEDED'::compensation_status,
                effective_end_date    = @EndDate,
                last_update_timestamp = @Now,
                last_updated_by       = @UpdatedBy
            WHERE employment_id     = @EmploymentId
              AND primary_rate_flag  = true
              AND compensation_status = 'ACTIVE'
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
    Task<OrgUnit?>             GetByIdAsync(Guid orgUnitId);
    Task<IEnumerable<OrgUnit>> GetByTypeAsync(OrgUnitType type);
    Task<IEnumerable<OrgUnit>> GetChildrenAsync(Guid parentOrgUnitId);
    Task<IEnumerable<OrgUnit>> GetAllActiveAsync();
    Task<Guid>                 InsertAsync(OrgUnit orgUnit, IUnitOfWork uow);
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

    public async Task<IEnumerable<OrgUnit>> GetByTypeAsync(OrgUnitType type)
    {
        const string sql = """
            SELECT * FROM org_unit
            WHERE org_unit_type = @Type::org_unit_type AND org_status = 'ACTIVE'
            ORDER BY org_unit_name
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<OrgUnit>(sql, new { Type = type.ToString().ToUpperInvariant() });
    }

    public async Task<IEnumerable<OrgUnit>> GetChildrenAsync(Guid parentOrgUnitId)
    {
        const string sql = """
            SELECT * FROM org_unit
            WHERE parent_org_unit_id = @ParentId AND org_status = 'ACTIVE'
            ORDER BY org_unit_name
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<OrgUnit>(sql, new { ParentId = parentOrgUnitId });
    }

    public async Task<IEnumerable<OrgUnit>> GetAllActiveAsync()
    {
        const string sql = "SELECT * FROM org_unit WHERE org_status = 'ACTIVE' ORDER BY org_unit_name";
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<OrgUnit>(sql);
    }

    public async Task<Guid> InsertAsync(OrgUnit orgUnit, IUnitOfWork uow)
    {
        const string sql = """
            INSERT INTO org_unit (
                org_unit_id, org_unit_type, org_unit_code, org_unit_name, parent_org_unit_id,
                org_status, effective_start_date, effective_end_date, tax_registration_number,
                country_code, state_of_incorporation, legal_entity_type, address_line_1,
                address_line_2, city, state_code, postal_code, locality_code, work_location_type,
                created_by, creation_timestamp, last_updated_by, last_update_timestamp
            ) VALUES (
                @OrgUnitId, @OrgUnitType::org_unit_type, @OrgUnitCode, @OrgUnitName,
                @ParentOrgUnitId, @OrgStatus::org_status, @EffectiveStartDate, @EffectiveEndDate,
                @TaxRegistrationNumber, @CountryCode, @StateOfIncorporation, @LegalEntityType,
                @AddressLine1, @AddressLine2, @City, @StateCode, @PostalCode, @LocalityCode,
                @WorkLocationType::work_location_type,
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
        const string sql = "SELECT * FROM job WHERE job_status = 'ACTIVE' ORDER BY job_title";
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<Job>(sql);
    }

    public async Task<Guid> InsertAsync(Job job, IUnitOfWork uow)
    {
        const string sql = """
            INSERT INTO job (
                job_id, job_code, job_title, job_family, job_level,
                flsa_classification, eeo_category, job_status,
                effective_start_date, effective_end_date,
                created_by, creation_timestamp, last_updated_by, last_update_timestamp
            ) VALUES (
                @JobId, @JobCode, @JobTitle, @JobFamily, @JobLevel,
                @FlsaClassification::flsa_classification, @EeoCategory::eeo_category,
                @JobStatus::job_status, @EffectiveStartDate, @EffectiveEndDate,
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
            WHERE job_id = @JobId AND position_status != 'CLOSED'
            ORDER BY position_status
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<Position>(sql, new { JobId = jobId });
    }

    public async Task<Guid> InsertAsync(Position position, IUnitOfWork uow)
    {
        const string sql = """
            INSERT INTO position (
                position_id, job_id, org_unit_id, position_title, headcount_budget,
                position_status, effective_start_date, effective_end_date,
                created_by, creation_timestamp, last_updated_by, last_update_timestamp
            ) VALUES (
                @PositionId, @JobId, @OrgUnitId, @PositionTitle, @HeadcountBudget,
                @PositionStatus::position_status, @EffectiveStartDate, @EffectiveEndDate,
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
    Task                             UpdateStatusAsync(Guid eventId, string status, IUnitOfWork uow);
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
                event_id, employment_id, event_type, effective_date,
                event_reason, notes, initiated_by, approved_by,
                approval_timestamp, creation_timestamp
            ) VALUES (
                @EventId, @EmploymentId, @EventType::employee_event_type, @EffectiveDate,
                @EventReason, @Notes, @InitiatedBy, @ApprovedBy,
                @ApprovalTimestamp, @CreationTimestamp
            )
            """;
        await uow.Connection.ExecuteAsync(sql, employeeEvent, uow.Transaction);
        return employeeEvent.EventId;
    }

    public Task UpdateStatusAsync(Guid eventId, string status, IUnitOfWork uow)
    {
        // Employee events are immutable — status updates are not supported in v1
        throw new NotSupportedException(
            "Employee events are immutable. Create a new event to record a status change.");
    }
}
