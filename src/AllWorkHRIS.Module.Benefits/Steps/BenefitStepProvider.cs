using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Module.Benefits.Domain.Codes;
using AllWorkHRIS.Module.Benefits.Domain.Elections;
using AllWorkHRIS.Module.Benefits.Repositories;
using AllWorkHRIS.Module.Benefits.Steps.Calculators;

namespace AllWorkHRIS.Module.Benefits.Steps;

public sealed class BenefitStepProvider : IBenefitStepProvider
{
    private readonly IBenefitElectionRepository        _electionRepository;
    private readonly IDeductionRateTableRepository     _rateTableRepository;
    private readonly IDeductionEmployerMatchRepository _matchRepository;
    private readonly BenefitCalculatorFactory          _calculatorFactory;

    public BenefitStepProvider(
        IBenefitElectionRepository        electionRepository,
        IDeductionRateTableRepository     rateTableRepository,
        IDeductionEmployerMatchRepository matchRepository,
        BenefitCalculatorFactory          calculatorFactory)
    {
        _electionRepository  = electionRepository;
        _rateTableRepository = rateTableRepository;
        _matchRepository     = matchRepository;
        _calculatorFactory   = calculatorFactory;
    }

    public async Task<IReadOnlyList<ICalculationStep>> GetStepsForEmployeeAsync(
        PipelineRequest request, CancellationToken ct = default)
    {
        // Fall back to single-day period when caller hasn't supplied boundaries.
        var periodStart = request.PayPeriodStart == default ? request.PayDate : request.PayPeriodStart;
        var periodEnd   = request.PayPeriodEnd   == default ? request.PayDate : request.PayPeriodEnd;

        var elections = (await _electionRepository.GetElectionsOverlappingPeriodAsync(
            request.EmploymentId, periodStart, periodEnd, ct)).ToList();

        if (elections.Count == 0) return [];

        var steps   = new List<ICalculationStep>(elections.Count * 2);
        int preSeq  = 110;
        int postSeq = 810;

        foreach (var election in elections)
        {
            if (election.Status != ElectionStatus.Active) continue;

            var fraction = ComputeCoverageFraction(
                election, periodStart, periodEnd, request.PartialPeriodRule);

            if (fraction == 0m) continue;

            var stepCode = election.DeductionCode.ToUpperInvariant();

            // PCT_POST_TAX (e.g. Roth 401k): amount depends on IncomeTaxableWages at execute
            // time, after all pre-tax deductions have run — PostTaxPctBenefitStep owns that logic.
            if (election.CalculationMode == CalculationMode.PctPostTax)
            {
                var rate = election.ContributionPct ?? 0m;
                steps.Add(new PostTaxPctBenefitStep(stepCode, postSeq++, rate, fraction));

                var postMatch = await LoadMatchRuleAsync(election, request.PayDate, ct);
                if (postMatch is not null)
                    steps.Add(BuildMatchStep(stepCode, postSeq++, postMatch, request));

                continue;
            }

            // All other modes: static per-period amount computed now.
            DeductionRateEntry? rateEntry = null;
            if (election.CalculationMode == CalculationMode.CoverageBased)
                rateEntry = await LoadRateEntryAsync(election, request.PayDate, ct);

            var calculator = _calculatorFactory.GetCalculator(election.CalculationMode);
            var amounts    = calculator.Compute(election, request, rateEntry);

            var eeAmount = Math.Round(amounts.Employee * fraction, 4);
            var erAmount = amounts.Employer.HasValue
                ? Math.Round(amounts.Employer.Value * fraction, 4)
                : (decimal?)null;

            if (election.TaxTreatment == TaxTreatment.PreTax)
            {
                steps.Add(new PreTaxBenefitStep(
                    stepCode, preSeq++,
                    eeAmount, erAmount,
                    reducesIncomeTax: true, reducesFica: false));

                var preMatch = await LoadMatchRuleAsync(election, request.PayDate, ct);
                if (preMatch is not null)
                    steps.Add(BuildMatchStep(stepCode, preSeq++, preMatch, request));
            }
            else
            {
                steps.Add(new PostTaxBenefitStep(stepCode, postSeq++, eeAmount, erAmount));

                var postMatch = await LoadMatchRuleAsync(election, request.PayDate, ct);
                if (postMatch is not null)
                    steps.Add(BuildMatchStep(stepCode, postSeq++, postMatch, request));
            }
        }

        return steps;
    }

    private async Task<DeductionRateEntry?> LoadRateEntryAsync(
        BenefitDeductionElection election, DateOnly asOf, CancellationToken ct)
    {
        var table = await _rateTableRepository.GetActiveByDeductionIdAsync(election.DeductionId, asOf, ct);
        if (table is null) return null;

        var entries = await _rateTableRepository.GetEntriesAsync(table.RateTableId, ct);
        return entries.FirstOrDefault(e => e.TierCode == election.CoverageTier);
    }

    private Task<DeductionEmployerMatch?> LoadMatchRuleAsync(
        BenefitDeductionElection election, DateOnly asOf, CancellationToken ct)
        => _matchRepository.GetActiveByDeductionIdAsync(election.DeductionId, asOf, employeeGroupId: null, ct);

    private static MatchBenefitStep BuildMatchStep(
        string stepCode, int seq, DeductionEmployerMatch matchRule, PipelineRequest request)
        => new(stepCode + "_MATCH", seq, stepCode, matchRule.MatchRate, ComputeMatchPeriodCap(matchRule, request));

    // Derives the per-period ceiling on employee contribution eligible for employer match.
    // Annual cap takes precedence; falls back to gross-percentage cap; uncapped if neither is set.
    private static decimal ComputeMatchPeriodCap(DeductionEmployerMatch matchRule, PipelineRequest request)
    {
        if (matchRule.MatchCapAnnualAmount.HasValue && request.PayPeriodsPerYear > 0)
            return Math.Round(matchRule.MatchCapAnnualAmount.Value / request.PayPeriodsPerYear, 4);

        if (matchRule.MatchCapPctOfGross.HasValue)
            return Math.Round(request.GrossPayPeriod * matchRule.MatchCapPctOfGross.Value, 4);

        return decimal.MaxValue;
    }

    private static decimal ComputeCoverageFraction(
        BenefitDeductionElection election,
        DateOnly periodStart, DateOnly periodEnd,
        string partialPeriodRule)
    {
        if (election.EffectiveStartDate <= periodStart
            && (election.EffectiveEndDate is null || election.EffectiveEndDate.Value >= periodEnd))
            return 1m;

        if (partialPeriodRule == "FULL_PERIOD")       return 1m;
        if (partialPeriodRule == "FIRST_FULL_PERIOD") return 0m;

        // PRORATE_DAYS: coverage_days / period_days
        var coverageStart = election.EffectiveStartDate > periodStart ? election.EffectiveStartDate : periodStart;
        var coverageEnd   = election.EffectiveEndDate.HasValue && election.EffectiveEndDate.Value < periodEnd
                            ? election.EffectiveEndDate.Value : periodEnd;
        var coverageDays  = (coverageEnd.DayNumber - coverageStart.DayNumber) + 1;
        var periodDays    = (periodEnd.DayNumber - periodStart.DayNumber) + 1;
        return Math.Min(1m, (decimal)coverageDays / periodDays);
    }
}
