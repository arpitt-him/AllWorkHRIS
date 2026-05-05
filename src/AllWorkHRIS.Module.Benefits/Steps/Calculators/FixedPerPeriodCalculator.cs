using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Module.Benefits.Domain.Codes;
using AllWorkHRIS.Module.Benefits.Domain.Elections;

namespace AllWorkHRIS.Module.Benefits.Steps.Calculators;

// EmployeeAmount on the election is already the per-period amount — pass through directly.
public sealed class FixedPerPeriodCalculator : IBenefitCalculator
{
    public string Mode => CalculationMode.FixedPerPeriod;

    public PerPeriodAmounts Compute(
        BenefitDeductionElection election,
        PipelineRequest          request,
        DeductionRateEntry?      rateEntry)
        => new(election.EmployeeAmount, election.EmployerContributionAmount);
}
