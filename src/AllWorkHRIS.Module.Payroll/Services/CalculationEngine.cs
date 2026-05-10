using Microsoft.Extensions.Logging;
using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Module.Payroll.Domain.Results;
using AllWorkHRIS.Module.Payroll.Repositories;

namespace AllWorkHRIS.Module.Payroll.Services;

public sealed partial class CalculationEngine : ICalculationEngine
{
    private readonly IResultLineRepository         _resultLineRepo;
    private readonly IPayrollPipelineService       _pipeline;
    private readonly IEmploymentJurisdictionLookup _jurisdictionLookup;
    private readonly IBenefitStepProvider          _benefitStepProvider;
    private readonly ILogger<CalculationEngine>    _logger;

    public CalculationEngine(
        IResultLineRepository         resultLineRepo,
        IPayrollPipelineService       pipeline,
        IEmploymentJurisdictionLookup jurisdictionLookup,
        IBenefitStepProvider          benefitStepProvider,
        ILogger<CalculationEngine>    logger)
    {
        _resultLineRepo      = resultLineRepo;
        _pipeline            = pipeline;
        _jurisdictionLookup  = jurisdictionLookup;
        _benefitStepProvider = benefitStepProvider;
        _logger              = logger;
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Calculating pay for employment {EmploymentId} (result {EmployeePayrollResultId})")]
    private partial void LogCalculationStart(Guid employmentId, Guid employeePayrollResultId);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Calculation complete for employment {EmploymentId}: gross={GrossPay:F4} net={NetPay:F4}")]
    private partial void LogCalculationComplete(Guid employmentId, decimal grossPay, decimal netPay);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Calculation failed for employment {EmploymentId} (result {EmployeePayrollResultId}): {Reason}")]
    private partial void LogCalculationFailed(Guid employmentId, Guid employeePayrollResultId, string reason);

    public async Task<CalculationOutput> CalculateAsync(CalculationInput input, CancellationToken ct = default)
    {
        var employeePayrollResultId = input.EmployeePayrollResultId;
        LogCalculationStart(input.EmploymentId, employeePayrollResultId);

        try
        {
            // Step 1 — Base earnings (regular/salary)
            var baseEarnings = await StepBaseEarningsAsync(input, employeePayrollResultId, ct);
            foreach (var line in baseEarnings)
                await _resultLineRepo.InsertEarningsLineAsync(line);

            ct.ThrowIfCancellationRequested();

            // Step 2 — Premium earnings (overtime, holiday, shift differential)
            var premiumEarnings = await StepPremiumEarningsAsync(input, employeePayrollResultId, ct);
            foreach (var line in premiumEarnings)
                await _resultLineRepo.InsertEarningsLineAsync(line);

            ct.ThrowIfCancellationRequested();

            // Cash gross before imputed income — used as the benefit calculation base.
            // PostTaxPctBenefitStep reads IncomeTaxableWages from the context, which starts here
            // and is reduced by pre-tax steps before the post-tax percent step runs.
            var cashGross = baseEarnings.Sum(l => l.CalculatedAmount)
                          + premiumEarnings.Sum(l => l.CalculatedAmount);

            // Fetch benefit steps once — shared by pre-tax, post-tax, and employer contribution passes.
            var benefitRequest = new PipelineRequest
            {
                EmploymentId      = input.EmploymentId,
                EmployeeId        = input.PersonId,
                PayrollContextId  = input.PayrollContextId,
                PeriodId          = input.PeriodId,
                PayDate           = input.PayDate,
                GrossPayPeriod    = cashGross,
                PayPeriodsPerYear = input.PeriodsPerYear,
                PayPeriodStart    = input.PayPeriodStart,
                PayPeriodEnd      = input.PayPeriodEnd,
                PartialPeriodRule = input.PartialPeriodRule,
                JurisdictionCode  = string.Empty
            };

            var allBenefitSteps = await _benefitStepProvider.GetStepsForEmployeeAsync(benefitRequest, ct);

            // Run all benefit steps in sequence order on a single context.
            // Pre-tax steps (seq < 800) reduce IncomeTaxableWages; post-tax PCT steps (seq ≥ 800)
            // then read the already-reduced wage base — ordering handles the dependency.
            var benefitCtx = new CalculationContext
            {
                EmployeeId         = input.PersonId,
                PayrollContextId   = input.PayrollContextId,
                PeriodId           = input.PeriodId,
                PayDate            = input.PayDate,
                PayPeriodsPerYear  = input.PeriodsPerYear,
                PayPeriodStart     = input.PayPeriodStart,
                PayPeriodEnd       = input.PayPeriodEnd,
                PartialPeriodRule  = input.PartialPeriodRule,
                GrossPayPeriod     = cashGross,
                IncomeTaxableWages = cashGross,
                FicaTaxableWages   = cashGross,
                NetPay             = cashGross,
                JurisdictionCode   = string.Empty
            };

            foreach (var step in allBenefitSteps.OrderBy(s => s.SequenceNumber))
                benefitCtx = await step.ExecuteAsync(benefitCtx, ct);

            // Step 3 — Pre-tax deductions
            var preTaxDeductions = StepPreTaxDeductions(
                input, employeePayrollResultId, allBenefitSteps, benefitCtx);
            foreach (var line in preTaxDeductions)
                await _resultLineRepo.InsertDeductionLineAsync(line);

            ct.ThrowIfCancellationRequested();

            // Step 4 — Imputed income (increases taxable wage base, not a cash payment)
            var imputedIncome = await StepImputedIncomeAsync(input, employeePayrollResultId, ct);
            foreach (var line in imputedIncome)
                await _resultLineRepo.InsertEarningsLineAsync(line);

            // Step 5 — Taxable wage determination
            var allEarnings  = baseEarnings.Concat(premiumEarnings).Concat(imputedIncome).ToList();
            var grossPay     = allEarnings.Sum(l => l.CalculatedAmount);
            var preTaxTotal  = preTaxDeductions.Sum(l => l.CalculatedAmount);
            // Clamp to zero: pre-tax deductions cannot exceed available earnings.
            // Negative taxable wages are meaningless to the tax pipeline and produce
            // negative net pay — the excess is flagged as NET_PAY_FLOOR_APPLIED.
            var taxableWages = Math.Max(grossPay - preTaxTotal, 0m);

            ct.ThrowIfCancellationRequested();

            // Step 6 — Tax withholdings (federal, state, local)
            var taxLines = await StepTaxWithholdingsAsync(input, employeePayrollResultId, taxableWages, ct);
            foreach (var line in taxLines)
                await _resultLineRepo.InsertTaxLineAsync(line);

            ct.ThrowIfCancellationRequested();

            // Step 7 — Post-tax deductions
            var postTaxDeductions = StepPostTaxDeductions(
                input, employeePayrollResultId, allBenefitSteps, benefitCtx);
            foreach (var line in postTaxDeductions)
                await _resultLineRepo.InsertDeductionLineAsync(line);

            ct.ThrowIfCancellationRequested();

            // Step 8 — Employer contributions (benefit ER amounts + match steps)
            var employerContributions = StepEmployerContributions(
                input, employeePayrollResultId, benefitCtx);
            foreach (var line in employerContributions)
                await _resultLineRepo.InsertEmployerContributionLineAsync(line);

            // Step 9 — Net pay
            var totalDeductions  = preTaxDeductions.Concat(postTaxDeductions).Sum(l => l.CalculatedAmount);
            var totalTax         = taxLines.Where(l => !l.EmployerFlag).Sum(l => l.CalculatedAmount);
            var totalContribs    = employerContributions.Sum(l => l.CalculatedAmount)
                                 + taxLines.Where(l => l.EmployerFlag).Sum(l => l.CalculatedAmount);
            var rawNetPay        = grossPay - totalDeductions - totalTax;
            var netPay           = Math.Max(rawNetPay, 0m);
            var floorApplied     = rawNetPay < 0m;
            var floorExcess      = floorApplied ? -rawNetPay : 0m;

            if (floorApplied)
                _logger.LogWarning(
                    "NET_PAY_FLOOR_APPLIED for employment {EmploymentId}: " +
                    "raw net={RawNet:F4} excess={Excess:F4}",
                    input.EmploymentId, rawNetPay, floorExcess);

            LogCalculationComplete(input.EmploymentId, grossPay, netPay);

            return new CalculationOutput
            {
                EmployeePayrollResultId    = employeePayrollResultId,
                Succeeded                  = true,
                GrossPay                   = grossPay,
                TotalDeductionsAmount      = totalDeductions,
                TotalEmployeeTaxAmount     = totalTax,
                TotalEmployerContribAmount = totalContribs,
                NetPay                     = netPay,
                NetPayFloorApplied         = floorApplied,
                NetPayFloorExcess          = floorExcess
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogCalculationFailed(input.EmploymentId, employeePayrollResultId, ex.Message);

            return new CalculationOutput
            {
                EmployeePayrollResultId    = employeePayrollResultId,
                Succeeded                  = false,
                FailureReason              = ex.Message,
                GrossPay                   = 0m,
                TotalDeductionsAmount      = 0m,
                TotalEmployeeTaxAmount     = 0m,
                TotalEmployerContribAmount = 0m,
                NetPay                     = 0m
            };
        }
    }

    // ── Earnings steps ───────────────────────────────────────────────────────

    private Task<IReadOnlyList<EarningsResultLine>> StepBaseEarningsAsync(
        CalculationInput input, Guid resultId, CancellationToken ct)
    {
        if (input.AnnualEquivalent is null or 0m || input.PeriodsPerYear == 0)
            return Task.FromResult<IReadOnlyList<EarningsResultLine>>([]);

        var periodAmount = Math.Round(input.AnnualEquivalent.Value / input.PeriodsPerYear, 4,
                               MidpointRounding.AwayFromZero);

        IReadOnlyList<EarningsResultLine> lines =
        [
            new EarningsResultLine
            {
                EarningsResultLineId    = Guid.NewGuid(),
                EmployeePayrollResultId = resultId,
                EmploymentId            = input.EmploymentId,
                EarningsCode            = "REG",
                EarningsDescription     = "Regular Salary",
                Quantity                = null,
                Rate                    = input.AnnualEquivalent.Value,
                CalculatedAmount        = periodAmount,
                JurisdictionSplitFlag   = false,
                TaxableFlag             = true,
                AccumulatorImpactFlag   = true,
                SourceRuleVersionId     = null,
                CorrectionFlag          = false,
                CorrectsLineId          = null,
                CreationTimestamp       = DateTimeOffset.UtcNow
            }
        ];
        return Task.FromResult(lines);
    }

    private Task<IReadOnlyList<EarningsResultLine>> StepPremiumEarningsAsync(
        CalculationInput input, Guid resultId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<EarningsResultLine>>([]);

    private Task<IReadOnlyList<EarningsResultLine>> StepImputedIncomeAsync(
        CalculationInput input, Guid resultId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<EarningsResultLine>>([]);

    // ── Benefit deduction steps ──────────────────────────────────────────────

    private static IReadOnlyList<DeductionResultLine> StepPreTaxDeductions(
        CalculationInput input, Guid resultId,
        IReadOnlyList<ICalculationStep> benefitSteps, CalculationContext benefitCtx)
    {
        var now   = DateTimeOffset.UtcNow;
        var lines = new List<DeductionResultLine>();

        foreach (var step in benefitSteps.Where(s => s.SequenceNumber < 800
                                                   && s.AppliesTo != StepAppliesTo.Employer))
        {
            if (!benefitCtx.StepResults.TryGetValue(step.StepCode, out var amount) || amount == 0)
                continue;

            lines.Add(new DeductionResultLine
            {
                DeductionResultLineId   = Guid.NewGuid(),
                EmployeePayrollResultId = resultId,
                EmploymentId            = input.EmploymentId,
                DeductionCode           = step.StepCode,
                DeductionDescription    = step.StepCode,
                CalculatedAmount        = amount,
                PreTaxFlag              = true,
                CashImpactFlag          = true,
                AccumulatorImpactFlag   = true,
                SourceRuleVersionId     = null,
                CorrectionFlag          = false,
                CorrectsLineId          = null,
                CreationTimestamp       = now
            });
        }

        return lines;
    }

    private static IReadOnlyList<DeductionResultLine> StepPostTaxDeductions(
        CalculationInput input, Guid resultId,
        IReadOnlyList<ICalculationStep> benefitSteps, CalculationContext benefitCtx)
    {
        var now   = DateTimeOffset.UtcNow;
        var lines = new List<DeductionResultLine>();

        foreach (var step in benefitSteps.Where(s => s.SequenceNumber >= 800
                                                   && s.AppliesTo != StepAppliesTo.Employer))
        {
            if (!benefitCtx.StepResults.TryGetValue(step.StepCode, out var amount) || amount == 0)
                continue;

            lines.Add(new DeductionResultLine
            {
                DeductionResultLineId   = Guid.NewGuid(),
                EmployeePayrollResultId = resultId,
                EmploymentId            = input.EmploymentId,
                DeductionCode           = step.StepCode,
                DeductionDescription    = step.StepCode,
                CalculatedAmount        = amount,
                PreTaxFlag              = false,
                CashImpactFlag          = true,
                AccumulatorImpactFlag   = true,
                SourceRuleVersionId     = null,
                CorrectionFlag          = false,
                CorrectsLineId          = null,
                CreationTimestamp       = now
            });
        }

        return lines;
    }

    private static IReadOnlyList<EmployerContributionResultLine> StepEmployerContributions(
        CalculationInput input, Guid resultId, CalculationContext benefitCtx)
    {
        var now   = DateTimeOffset.UtcNow;
        var lines = new List<EmployerContributionResultLine>();

        foreach (var (code, amount) in benefitCtx.EmployerStepResults)
        {
            if (amount == 0) continue;

            lines.Add(new EmployerContributionResultLine
            {
                EmployerContributionResultLineId = Guid.NewGuid(),
                EmployeePayrollResultId          = resultId,
                EmploymentId                     = input.EmploymentId,
                ContributionCode                 = code,
                ContributionDescription          = code,
                CalculatedAmount                 = amount,
                AccumulatorImpactFlag            = true,
                SourceRuleVersionId              = null,
                CorrectionFlag                   = false,
                CorrectsLineId                   = null,
                CreationTimestamp                = now
            });
        }

        return lines;
    }

    // ── Tax step ─────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<TaxResultLine>> StepTaxWithholdingsAsync(
        CalculationInput input, Guid resultId, decimal taxableWages, CancellationToken ct)
    {
        var jurisdictions = await _jurisdictionLookup.GetJurisdictionsAsync(
            input.EmploymentId, input.PayDate, ct);

        if (jurisdictions.Count == 0) return [];

        var lines = new List<TaxResultLine>();
        var now   = DateTimeOffset.UtcNow;

        foreach (var jur in jurisdictions)
        {
            var request = new PipelineRequest
            {
                EmploymentId      = input.EmploymentId,
                EmployeeId        = input.PersonId,
                PayrollContextId  = input.PayrollContextId,
                PeriodId          = input.PeriodId,
                PayDate           = input.PayDate,
                GrossPayPeriod    = taxableWages,
                PayPeriodsPerYear = input.PeriodsPerYear,
                JurisdictionCode  = jur.JurisdictionCode,
                SkipBenefitSteps  = true
            };

            var result = await _pipeline.RunAsync(request, ct);
            if (!result.Succeeded)
            {
                _logger.LogWarning(
                    "Tax pipeline failed for employment {EmploymentId} jurisdiction {JurisdictionCode}: {Reason}",
                    input.EmploymentId, jur.JurisdictionCode, result.FailureReason);
                continue;
            }

            foreach (var (stepCode, amount) in result.StepResults)
            {
                lines.Add(new TaxResultLine
                {
                    TaxResultLineId         = Guid.NewGuid(),
                    EmployeePayrollResultId = resultId,
                    EmploymentId            = input.EmploymentId,
                    JurisdictionId          = Guid.Empty,
                    TaxCode                 = stepCode,
                    TaxDescription          = stepCode,
                    TaxableWagesAmount      = taxableWages,
                    CalculatedAmount        = amount,
                    EmployerFlag            = false,
                    AccumulatorImpactFlag   = true,
                    SourceRuleVersionId     = null,
                    CorrectionFlag          = false,
                    CorrectsLineId          = null,
                    CreationTimestamp       = now
                });
            }

            foreach (var (stepCode, amount) in result.EmployerStepResults)
            {
                lines.Add(new TaxResultLine
                {
                    TaxResultLineId         = Guid.NewGuid(),
                    EmployeePayrollResultId = resultId,
                    EmploymentId            = input.EmploymentId,
                    JurisdictionId          = Guid.Empty,
                    TaxCode                 = stepCode,
                    TaxDescription          = stepCode,
                    TaxableWagesAmount      = taxableWages,
                    CalculatedAmount        = amount,
                    EmployerFlag            = true,
                    AccumulatorImpactFlag   = true,
                    SourceRuleVersionId     = null,
                    CorrectionFlag          = false,
                    CorrectsLineId          = null,
                    CreationTimestamp       = now
                });
            }
        }

        return lines;
    }
}
