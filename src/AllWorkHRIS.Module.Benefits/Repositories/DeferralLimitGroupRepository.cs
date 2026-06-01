using Dapper;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Benefits.Domain.Codes;

namespace AllWorkHRIS.Module.Benefits.Repositories;

public sealed class DeferralLimitGroupRepository : IDeferralLimitGroupRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public DeferralLimitGroupRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<IReadOnlyList<DeferralLimitGroup>> GetActiveGroupsAsync(
        DateOnly asOf, Guid? legalEntityId = null, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.CreateConnection();

        // Candidate group rows active on asOf: the global default (legal_entity_id IS NULL) plus,
        // when an entity is supplied, that entity's overrides. We resolve the winner per code below.
        const string groupSql = """
            SELECT group_code            AS GroupCode,
                   group_name            AS GroupName,
                   legal_entity_id       AS LegalEntityId,
                   limit_amount          AS LimitAmount,
                   catch_up_amount       AS CatchUpAmount,
                   plan_year_start_month AS PlanYearStartMonth,
                   plan_year_start_day   AS PlanYearStartDay,
                   effective_from        AS EffectiveFrom,
                   effective_to          AS EffectiveTo
            FROM   deferral_limit_group
            WHERE  effective_from <= @AsOf
              AND  (effective_to IS NULL OR effective_to >= @AsOf)
              AND  (legal_entity_id IS NULL OR legal_entity_id = @LegalEntityId)
            """;

        var candidates = (await conn.QueryAsync<GroupRow>(
            groupSql, new { AsOf = asOf, LegalEntityId = legalEntityId })).ToList();

        if (candidates.Count == 0) return [];

        // Active member sets, keyed by group code.
        const string memberSql = """
            SELECT group_code, member_accumulator_code
            FROM   deferral_limit_group_member
            WHERE  effective_from <= @AsOf
              AND  (effective_to IS NULL OR effective_to >= @AsOf)
            """;
        var memberRows = await conn.QueryAsync<(string group_code, string member_accumulator_code)>(
            memberSql, new { AsOf = asOf });

        var membersByCode = memberRows
            .GroupBy(m => m.group_code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(m => m.member_accumulator_code).ToList(),
                StringComparer.OrdinalIgnoreCase);

        // Resolve one winner per group code: an entity-specific row beats the global default,
        // then the latest effective_from. Mirrors the employer-match trust override.
        var resolved = candidates
            .GroupBy(c => c.GroupCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(c => c.LegalEntityId.HasValue)   // entity override first
                .ThenByDescending(c => c.EffectiveFrom)
                .First())
            .Select(c => new DeferralLimitGroup
            {
                GroupCode          = c.GroupCode,
                GroupName          = c.GroupName,
                LegalEntityId      = c.LegalEntityId,
                LimitAmount        = c.LimitAmount,
                CatchUpAmount      = c.CatchUpAmount,
                PlanYearStartMonth = c.PlanYearStartMonth,
                PlanYearStartDay   = c.PlanYearStartDay,
                EffectiveFrom      = c.EffectiveFrom,
                EffectiveTo        = c.EffectiveTo,
                MemberAccumulatorCodes = membersByCode.TryGetValue(c.GroupCode, out var members)
                    ? members
                    : [],
            })
            .ToList();

        return resolved;
    }

    private sealed record GroupRow
    {
        public string   GroupCode          { get; init; } = "";
        public string   GroupName          { get; init; } = "";
        public Guid?    LegalEntityId      { get; init; }
        public decimal  LimitAmount        { get; init; }
        public decimal? CatchUpAmount      { get; init; }
        public int      PlanYearStartMonth { get; init; }
        public int      PlanYearStartDay   { get; init; }
        public DateOnly EffectiveFrom      { get; init; }
        public DateOnly? EffectiveTo       { get; init; }
    }
}
