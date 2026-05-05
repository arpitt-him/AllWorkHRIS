using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Module.Benefits.Domain.Codes;
using AllWorkHRIS.Module.Benefits.Domain.Elections;

namespace AllWorkHRIS.Module.Benefits.Steps.Calculators;

// EmployeeAmount on the election is the monthly target.
// ThreePaycheckMonthRule governs the amount in months with more than 2 pay dates.
//   PRORATE  — equal share across all pay dates in the month (default)
//   SKIP     — zero amount for the 3rd+ paycheck in the month
//   anything else — always divide by 2, ignoring the extra period (net-zero for employee)
public sealed class FixedMonthlyCalculator : IBenefitCalculator
{
    public string Mode => CalculationMode.FixedMonthly;

    public PerPeriodAmounts Compute(
        BenefitDeductionElection election,
        PipelineRequest          request,
        DeductionRateEntry?      rateEntry)
    {
        var divisor = ComputeDivisor(request);
        if (divisor == 0m) return new(0m, null);

        var eeAmount = Math.Round(election.EmployeeAmount / divisor, 4);
        var erAmount = election.EmployerContributionAmount.HasValue
            ? Math.Round(election.EmployerContributionAmount.Value / divisor, 4)
            : (decimal?)null;

        return new(eeAmount, erAmount);
    }

    private static decimal ComputeDivisor(PipelineRequest request)
    {
        var payDates = Math.Max(1, request.PayDatesInPeriodMonth);

        if (payDates <= 2) return payDates;

        return request.ThreePaycheckMonthRule switch
        {
            "PRORATE" => payDates,
            "SKIP"    => request.PayDateOrdinalInMonth > 2 ? 0m : 2m,
            _         => 2m
        };
    }
}
