using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Module.Benefits.Domain.Codes;
using AllWorkHRIS.Module.Benefits.Domain.Elections;

namespace AllWorkHRIS.Module.Benefits.Steps.Calculators;

// EmployeeAmount on the election is the annual target; divide by pay periods per year.
public sealed class FixedAnnualCalculator : IBenefitCalculator
{
    public string Mode => CalculationMode.FixedAnnual;

    public PerPeriodAmounts Compute(
        BenefitDeductionElection election,
        PipelineRequest          request,
        DeductionRateEntry?      rateEntry)
    {
        if (request.PayPeriodsPerYear <= 0) return new(0m, null);

        var eeAmount = Math.Round(election.EmployeeAmount / request.PayPeriodsPerYear, 4);
        var erAmount = election.EmployerContributionAmount.HasValue
            ? Math.Round(election.EmployerContributionAmount.Value / request.PayPeriodsPerYear, 4)
            : (decimal?)null;

        return new(eeAmount, erAmount);
    }
}
