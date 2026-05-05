using Dapper;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Domain;
using AllWorkHRIS.Module.Benefits.Domain.Elections;
using AllWorkHRIS.Module.Benefits.Queries;

namespace AllWorkHRIS.Module.Benefits.Repositories;

public sealed class BenefitElectionRepository : IBenefitElectionRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public BenefitElectionRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    // Includes JOIN to deduction so d.code is available as deduction_code in all queries.
    private const string SelectBase = """
        SELECT e.election_id, e.employment_id, e.deduction_id,
               d.code             AS deduction_code,
               d.calculation_mode AS calculation_mode,
               e.tax_treatment, e.employee_amount, e.employer_contribution_amount,
               e.contribution_pct, e.coverage_tier,
               e.annual_coverage_amount, e.annual_election_amount, e.monthly_election_amount,
               e.effective_start_date, e.effective_end_date,
               e.status, e.source, e.created_by, e.created_at, e.updated_at,
               e.election_version_id, e.original_election_id, e.parent_election_id,
               e.correction_type, e.source_event_id
        FROM   benefit_deduction_election e
        JOIN   deduction d ON d.deduction_id = e.deduction_id
        """;

    public async Task<BenefitDeductionElection?> GetByIdAsync(Guid electionId, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var sql = $"{SelectBase} WHERE e.election_id = @ElectionId";
        return await conn.QuerySingleOrDefaultAsync<BenefitDeductionElection>(sql, new { ElectionId = electionId });
    }

    public async Task<IEnumerable<BenefitDeductionElection>> GetByEmploymentIdAsync(Guid employmentId, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var sql = $"""
            {SelectBase}
            WHERE  e.employment_id = @EmploymentId
            ORDER  BY e.created_at DESC
            """;
        return await conn.QueryAsync<BenefitDeductionElection>(sql, new { EmploymentId = employmentId });
    }

    public async Task<IEnumerable<BenefitDeductionElection>> GetActiveByEmploymentIdAsync(
        Guid employmentId, DateOnly asOf, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var sql = $"""
            {SelectBase}
            WHERE  e.employment_id        = @EmploymentId
              AND  e.status               = 'ACTIVE'
              AND  e.effective_start_date <= @AsOf
              AND  (e.effective_end_date IS NULL OR e.effective_end_date >= @AsOf)
            ORDER  BY d.code
            """;
        return await conn.QueryAsync<BenefitDeductionElection>(sql, new { EmploymentId = employmentId, AsOf = asOf });
    }

    public async Task<BenefitDeductionElection?> GetActiveByCodeAsync(
        Guid employmentId, string deductionCode, DateOnly asOf, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var sql = $"""
            {SelectBase}
            WHERE  e.employment_id        = @EmploymentId
              AND  d.code                 = @DeductionCode
              AND  e.status               = 'ACTIVE'
              AND  e.effective_start_date <= @AsOf
              AND  (e.effective_end_date IS NULL OR e.effective_end_date >= @AsOf)
            LIMIT 1
            """;
        return await conn.QuerySingleOrDefaultAsync<BenefitDeductionElection>(
            sql, new { EmploymentId = employmentId, DeductionCode = deductionCode, AsOf = asOf });
    }

    public async Task<IEnumerable<BenefitDeductionElection>> GetElectionsOverlappingPeriodAsync(
        Guid employmentId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var sql = $"""
            {SelectBase}
            WHERE  e.employment_id        = @EmploymentId
              AND  e.status              != 'SUPERSEDED'
              AND  e.effective_start_date <= @PeriodEnd
              AND  (e.effective_end_date IS NULL OR e.effective_end_date >= @PeriodStart)
            ORDER  BY d.code
            """;
        return await conn.QueryAsync<BenefitDeductionElection>(
            sql, new { EmploymentId = employmentId, PeriodStart = periodStart, PeriodEnd = periodEnd });
    }

    public async Task<IEnumerable<BenefitDeductionElection>> GetSuspendedByEmploymentIdAsync(
        Guid employmentId, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var sql = $"""
            {SelectBase}
            WHERE  e.employment_id = @EmploymentId
              AND  e.status        = 'SUSPENDED'
            """;
        return await conn.QueryAsync<BenefitDeductionElection>(sql, new { EmploymentId = employmentId });
    }

    public async Task<IEnumerable<BenefitDeductionElection>> GetNonSupersededByEmploymentIdsAsync(
        IReadOnlyList<Guid> employmentIds, DateOnly periodStart, DateOnly periodEnd,
        CancellationToken ct = default)
    {
        if (employmentIds.Count == 0) return [];
        using var conn = _connectionFactory.CreateConnection();
        var sql = $"""
            {SelectBase}
            WHERE  e.employment_id        = ANY(@EmploymentIds)
              AND  e.status              != 'SUPERSEDED'
              AND  e.effective_start_date <= @PeriodEnd
              AND  (e.effective_end_date IS NULL OR e.effective_end_date >= @PeriodStart)
            ORDER  BY e.employment_id, d.code
            """;
        return await conn.QueryAsync<BenefitDeductionElection>(
            sql, new { EmploymentIds = employmentIds.ToArray(), PeriodStart = periodStart, PeriodEnd = periodEnd });
    }

    public async Task<bool> HasOverlapAsync(
        Guid employmentId, Guid deductionId, DateOnly start, DateOnly? end,
        CancellationToken ct = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        long count;

        if (end.HasValue)
        {
            const string sql = """
                SELECT COUNT(*)
                FROM   benefit_deduction_election
                WHERE  employment_id        = @EmploymentId
                  AND  deduction_id         = @DeductionId
                  AND  status NOT IN ('SUPERSEDED', 'TERMINATED')
                  AND  effective_start_date <= @End
                  AND  (effective_end_date IS NULL OR effective_end_date >= @Start)
                """;
            count = await conn.ExecuteScalarAsync<long>(
                sql, new { EmploymentId = employmentId, DeductionId = deductionId, Start = start, End = end.Value });
        }
        else
        {
            const string sql = """
                SELECT COUNT(*)
                FROM   benefit_deduction_election
                WHERE  employment_id       = @EmploymentId
                  AND  deduction_id        = @DeductionId
                  AND  status NOT IN ('SUPERSEDED', 'TERMINATED')
                  AND  (effective_end_date IS NULL OR effective_end_date >= @Start)
                """;
            count = await conn.ExecuteScalarAsync<long>(
                sql, new { EmploymentId = employmentId, DeductionId = deductionId, Start = start });
        }

        return count > 0;
    }

    public async Task<bool> HasOverlapAsync(
        Guid employmentId, Guid deductionId, DateOnly start, DateOnly? end,
        IUnitOfWork uow, CancellationToken ct = default)
    {
        long count;

        if (end.HasValue)
        {
            const string sql = """
                SELECT COUNT(*)
                FROM   benefit_deduction_election
                WHERE  employment_id        = @EmploymentId
                  AND  deduction_id         = @DeductionId
                  AND  status NOT IN ('SUPERSEDED', 'TERMINATED')
                  AND  effective_start_date <= @End
                  AND  (effective_end_date IS NULL OR effective_end_date >= @Start)
                """;
            count = await uow.Connection.ExecuteScalarAsync<long>(
                sql,
                new { EmploymentId = employmentId, DeductionId = deductionId, Start = start, End = end.Value },
                uow.Transaction);
        }
        else
        {
            const string sql = """
                SELECT COUNT(*)
                FROM   benefit_deduction_election
                WHERE  employment_id        = @EmploymentId
                  AND  deduction_id         = @DeductionId
                  AND  status NOT IN ('SUPERSEDED', 'TERMINATED')
                  AND  (effective_end_date IS NULL OR effective_end_date >= @Start)
                """;
            count = await uow.Connection.ExecuteScalarAsync<long>(
                sql,
                new { EmploymentId = employmentId, DeductionId = deductionId, Start = start },
                uow.Transaction);
        }

        return count > 0;
    }

    public async Task<IEnumerable<BenefitDeductionElection>> GetOverlappingByDeductionAsync(
        Guid employmentId, Guid deductionId, DateOnly start, DateOnly? end,
        IUnitOfWork uow, CancellationToken ct = default)
    {
        if (end.HasValue)
        {
            var sql = $"""
                {SelectBase}
                WHERE  e.employment_id        = @EmploymentId
                  AND  e.deduction_id         = @DeductionId
                  AND  e.status NOT IN ('SUPERSEDED', 'TERMINATED')
                  AND  e.effective_start_date <= @End
                  AND  (e.effective_end_date IS NULL OR e.effective_end_date >= @Start)
                """;
            return await uow.Connection.QueryAsync<BenefitDeductionElection>(
                sql,
                new { EmploymentId = employmentId, DeductionId = deductionId, Start = start, End = end.Value },
                uow.Transaction);
        }
        else
        {
            var sql = $"""
                {SelectBase}
                WHERE  e.employment_id        = @EmploymentId
                  AND  e.deduction_id         = @DeductionId
                  AND  e.status NOT IN ('SUPERSEDED', 'TERMINATED')
                  AND  (e.effective_end_date IS NULL OR e.effective_end_date >= @Start)
                """;
            return await uow.Connection.QueryAsync<BenefitDeductionElection>(
                sql,
                new { EmploymentId = employmentId, DeductionId = deductionId, Start = start },
                uow.Transaction);
        }
    }

    public async Task<Guid> InsertAsync(BenefitDeductionElection election, IUnitOfWork uow)
    {
        const string sql = """
            INSERT INTO benefit_deduction_election
                (election_id, employment_id, deduction_id, tax_treatment,
                 employee_amount, employer_contribution_amount,
                 contribution_pct, coverage_tier,
                 annual_coverage_amount, annual_election_amount, monthly_election_amount,
                 effective_start_date, effective_end_date,
                 status, source, created_by, created_at, updated_at,
                 election_version_id, original_election_id, parent_election_id,
                 correction_type, source_event_id)
            VALUES
                (@ElectionId, @EmploymentId, @DeductionId, @TaxTreatment,
                 @EmployeeAmount, @EmployerContributionAmount,
                 @ContributionPct, @CoverageTier,
                 @AnnualCoverageAmount, @AnnualElectionAmount, @MonthlyElectionAmount,
                 @EffectiveStartDate, @EffectiveEndDate,
                 @Status, @Source, @CreatedBy, @CreatedAt, @UpdatedAt,
                 @ElectionVersionId, @OriginalElectionId, @ParentElectionId,
                 @CorrectionType, @SourceEventId)
            """;
        await uow.Connection.ExecuteAsync(sql, election, uow.Transaction);
        return election.ElectionId;
    }

    public async Task UpdateStatusAsync(Guid electionId, string status, IUnitOfWork uow)
    {
        const string sql = """
            UPDATE benefit_deduction_election
            SET    status     = @Status,
                   updated_at = now()
            WHERE  election_id = @ElectionId
            """;
        await uow.Connection.ExecuteAsync(sql, new { ElectionId = electionId, Status = status }, uow.Transaction);
    }

    public async Task UpdateStatusWithEventAsync(Guid electionId, string status, Guid sourceEventId, IUnitOfWork uow)
    {
        const string sql = """
            UPDATE benefit_deduction_election
            SET    status          = @Status,
                   source_event_id = @SourceEventId,
                   updated_at      = now()
            WHERE  election_id = @ElectionId
            """;
        await uow.Connection.ExecuteAsync(
            sql, new { ElectionId = electionId, Status = status, SourceEventId = sourceEventId }, uow.Transaction);
    }

    public async Task TrimEndDateAsync(Guid electionId, DateOnly newEndDate, IUnitOfWork uow)
    {
        const string sql = """
            UPDATE benefit_deduction_election
            SET    effective_end_date = @NewEndDate,
                   updated_at         = now()
            WHERE  election_id = @ElectionId
            """;
        await uow.Connection.ExecuteAsync(sql, new { ElectionId = electionId, NewEndDate = newEndDate }, uow.Transaction);
    }

    public async Task TerminateAsync(Guid electionId, DateOnly effectiveEndDate, IUnitOfWork uow)
    {
        const string sql = """
            UPDATE benefit_deduction_election
            SET    status             = 'TERMINATED',
                   effective_end_date = @EffectiveEndDate,
                   updated_at         = now()
            WHERE  election_id = @ElectionId
            """;
        await uow.Connection.ExecuteAsync(
            sql, new { ElectionId = electionId, EffectiveEndDate = effectiveEndDate }, uow.Transaction);
    }

    public async Task TerminateWithEventAsync(Guid electionId, DateOnly effectiveEndDate, Guid sourceEventId, IUnitOfWork uow)
    {
        const string sql = """
            UPDATE benefit_deduction_election
            SET    status             = 'TERMINATED',
                   effective_end_date = @EffectiveEndDate,
                   source_event_id    = @SourceEventId,
                   updated_at         = now()
            WHERE  election_id = @ElectionId
            """;
        await uow.Connection.ExecuteAsync(
            sql, new { ElectionId = electionId, EffectiveEndDate = effectiveEndDate, SourceEventId = sourceEventId }, uow.Transaction);
    }

    public async Task<int> ActivatePendingAsync(DateOnly asOf, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        const string sql = """
            UPDATE benefit_deduction_election
            SET    status     = 'ACTIVE',
                   updated_at = CURRENT_TIMESTAMP
            WHERE  status               = 'PENDING'
              AND  effective_start_date <= @AsOf
            """;
        return await conn.ExecuteAsync(sql, new { AsOf = asOf });
    }

    public async Task<PagedResult<ElectionListItem>> GetPagedListAsync(
        ElectionListQuery query, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.CreateConnection();

        var where = new List<string>();
        var p = new DynamicParameters();

        if (query.EmploymentId.HasValue)
        {
            where.Add("e.employment_id = @EmploymentId");
            p.Add("EmploymentId", query.EmploymentId.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.DeductionCode))
        {
            where.Add("d.code = @DeductionCode");
            p.Add("DeductionCode", query.DeductionCode);
        }
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            where.Add("e.status = @Status");
            p.Add("Status", query.Status);
        }
        if (query.EffectiveFrom.HasValue)
        {
            where.Add("e.effective_start_date >= @EffectiveFrom");
            p.Add("EffectiveFrom", query.EffectiveFrom.Value);
        }
        if (query.EffectiveTo.HasValue)
        {
            where.Add("e.effective_start_date <= @EffectiveTo");
            p.Add("EffectiveTo", query.EffectiveTo.Value);
        }

        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty;

        var countSql = $"""
            SELECT COUNT(*)
            FROM   benefit_deduction_election e
            JOIN   deduction d ON d.deduction_id = e.deduction_id
            {whereClause}
            """;

        var dataSql = $"""
            SELECT e.election_id, e.employment_id,
                   emp.employee_number,
                   p.legal_last_name || ', ' || p.legal_first_name AS employee_display_name,
                   d.code             AS deduction_code,
                   d.description      AS deduction_description,
                   d.calculation_mode AS calculation_mode,
                   e.tax_treatment, e.employee_amount, e.employer_contribution_amount,
                   e.contribution_pct, e.coverage_tier,
                   e.effective_start_date, e.effective_end_date,
                   e.status, e.source, e.created_at
            FROM   benefit_deduction_election e
            JOIN   deduction   d   ON d.deduction_id  = e.deduction_id
            JOIN   employment  emp ON emp.employment_id = e.employment_id
            JOIN   person      p   ON p.person_id = emp.person_id
            {whereClause}
            ORDER  BY e.created_at DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        p.Add("PageSize", query.PageSize);
        p.Add("Offset", (query.Page - 1) * query.PageSize);

        var totalCount = await conn.ExecuteScalarAsync<long>(countSql, p);
        var items      = (await conn.QueryAsync<ElectionListItem>(dataSql, p)).ToList();

        return new PagedResult<ElectionListItem>
        {
            Items      = items,
            TotalCount = (int)totalCount,
            Page       = query.Page,
            PageSize   = query.PageSize
        };
    }
}
