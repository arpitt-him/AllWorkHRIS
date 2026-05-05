using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Module.Benefits.Domain.Codes;
using AllWorkHRIS.Module.Benefits.Domain.Elections;

namespace AllWorkHRIS.Module.Benefits.Steps.Calculators;

// ContributionPct is stored as a decimal rate (e.g. 0.05 for 5%).
// Applied against GrossPayPeriod at step-creation time.
// WageBase segmentation (REGULAR_ONLY, ELIGIBLE_ONLY) requires earnings-type breakdowns
// that are not yet carried in PipelineRequest — deferred to Tier 7.
// Employer match is handled by MatchBenefitStep, not returned here.
public sealed class PctPreTaxCalculator : IBenefitCalculator
{
    public string Mode => CalculationMode.PctPreTax;

    public PerPeriodAmounts Compute(
        BenefitDeductionElection election,
        PipelineRequest          request,
        DeductionRateEntry?      rateEntry)
    {
        var rate     = election.ContributionPct ?? 0m;
        var eeAmount = Math.Round(request.GrossPayPeriod * rate, 4);
        return new(eeAmount, null);
    }
}
