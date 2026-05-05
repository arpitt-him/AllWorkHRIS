using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Module.Benefits.Domain.Codes;
using AllWorkHRIS.Module.Benefits.Domain.Elections;

namespace AllWorkHRIS.Module.Benefits.Steps.Calculators;

public readonly record struct PerPeriodAmounts(decimal Employee, decimal? Employer);

public interface IBenefitCalculator
{
    string Mode { get; }

    // Compute the raw per-period employee and employer amounts before the coverage
    // fraction is applied.  rateEntry is non-null only for COVERAGE_BASED mode.
    PerPeriodAmounts Compute(
        BenefitDeductionElection election,
        PipelineRequest          request,
        DeductionRateEntry?      rateEntry);
}
