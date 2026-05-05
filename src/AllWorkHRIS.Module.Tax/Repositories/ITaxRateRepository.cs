using AllWorkHRIS.Module.Tax.Queries;

namespace AllWorkHRIS.Module.Tax.Repositories;

public interface ITaxRateRepository
{
    Task<IReadOnlyList<CalculationStepRow>> GetActiveStepsAsync(
        string jurisdictionCode, DateOnly payDate, CancellationToken ct = default);

    Task<IReadOnlyList<BracketRow>> GetBracketsAsync(
        string stepCode, string? filingStatusCode, DateOnly payDate, CancellationToken ct = default);

    Task<FlatRateRow?> GetFlatRateAsync(
        string stepCode, DateOnly payDate, CancellationToken ct = default);

    Task<IReadOnlyList<TieredBracketRow>> GetTieredBracketsAsync(
        string stepCode, DateOnly payDate, CancellationToken ct = default);

    Task<AllowanceRow?> GetAllowanceAsync(
        string stepCode, string? filingStatusCode, DateOnly payDate, CancellationToken ct = default);

    Task<CreditRow?> GetCreditAsync(
        string stepCode, DateOnly payDate, CancellationToken ct = default);
}
