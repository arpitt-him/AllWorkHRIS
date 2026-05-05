using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Module.Benefits.Domain.Codes;
using AllWorkHRIS.Module.Benefits.Domain.Elections;

namespace AllWorkHRIS.Module.Benefits.Steps.Calculators;

// Rate entry is resolved by the provider (active rate table → entry matching CoverageTier)
// before Compute is called.  If no matching entry is found both amounts are zero.
// EmployeeRate / EmployerRate in the rate entry are already per-period amounts.
public sealed class CoverageBasedCalculator : IBenefitCalculator
{
    public string Mode => CalculationMode.CoverageBased;

    public PerPeriodAmounts Compute(
        BenefitDeductionElection election,
        PipelineRequest          request,
        DeductionRateEntry?      rateEntry)
    {
        if (rateEntry is null) return new(0m, null);
        return new(rateEntry.EmployeeRate, rateEntry.EmployerRate);
    }
}
