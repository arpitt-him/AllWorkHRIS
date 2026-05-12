using Microsoft.Extensions.Logging;
using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Module.Payroll.Domain.Results;
using AllWorkHRIS.Module.Payroll.Repositories;
using AllWorkHRIS.Module.TimeAttendance.Repositories;

namespace AllWorkHRIS.Module.Payroll.Services;

public sealed partial class CalculationEngine : ICalculationEngine
{
    private readonly IResultLineRepository         _resultLineRepo;
    private readonly IPayrollPipelineService       _pipeline;
    private readonly IEmploymentJurisdictionLookup _jurisdictionLookup;
    private readonly IBenefitStepProvider          _benefitStepProvider;
    private readonly IAccumulatorService           _accumulatorService;
    private readonly ITimeEntryRepository          _timeEntryRepo;
    private readonly ILogger<CalculationEngine>    _logger;

    public CalculationEngine(
        IResultLineRepository         resultLineRepo,
        IPayrollPipelineService       pipeline,
        IEmploymentJurisdictionLookup jurisdictionLookup,
        IBenefitStepProvider          benefitStepProvider,
        IAccumulatorService           accumulatorService,
        ITimeEntryRepository          timeEntryRepo,
        ILogger<CalculationEngine>    logger)
    {
        _resultLineRepo      = resultLineRepo;
        _pipeline            = pipeline;
        _jurisdictionLookup  = jurisdictionLookup;
        _benefitStepProvider = benefitStepProvider;
        _accumulatorService  = accumulatorService;
        _timeEntryRepo       = timeEntryRepo;
        _logger              = logger;
    }

    // Carries hours and rate for NON_EXEMPT employees through both earnings steps.
    private sealed record NonExemptPayData(decimal RegHours, decimal OtHours, decimal HourlyRate);

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
            // Resolve non-exempt pay data once — shared by Steps 1 and 2.
            var nonExemptData = await ResolveNonExemptDataAsync(input, ct);

            // Step 1 — Base earnings (regular/salary)
            var baseEarnings = await StepBaseEarningsAsync(input, employeePayrollResultId, nonExemptData, ct);
            foreach (var line in baseEarnings)
                await _resultLineRepo.InsertEarningsLineAsync(line);

            ct.ThrowIfCancellationRequested();

            // Step 2 — Premium earnings (overtime premium at 0.5× rate for NON_EXEMPT)
            var premiumEarnings = await StepPremiumEarningsAsync(input, employeePayrollResultId, nonExemptData, ct);
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

            // Stage 1 floor detection (Phase 10.6): pre-tax deductions consumed all earnings.
            // The tax pipeline receives 0 income-taxable wages; net pay will be clamped at Stage 2.
            if (benefitCtx.IncomeTaxableWages < 0m)
                _logger.LogWarning(
                    "NET_PAY_FLOOR (pre-tax) for employment {EmploymentId}: " +
                    "IncomeTaxableWages={IncomeTaxableWages:F4} — clamped to 0 for tax pipeline",
                    input.EmploymentId, benefitCtx.IncomeTaxableWages);

            ct.ThrowIfCancellationRequested();

            // Step 6 — Tax withholdings (federal, state, local)
            // benefitCtx.FicaTaxableWages reflects gross minus only FICA-exempt deductions;
            // taxableWages (income-taxable) reflects gross minus all pre-tax deductions.
            // Both are passed separately so the FICA steps use the correct wage base.
            // Guard against negative FICA base in the unlikely event FICA-exempt deductions exceed gross.
            var ficaTaxableWages = Math.Max(benefitCtx.FicaTaxableWages + (grossPay - cashGross), 0m);
            var taxLines = await StepTaxWithholdingsAsync(input, employeePayrollResultId, taxableWages, ficaTaxableWages, ct);
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

    private async Task<NonExemptPayData?> ResolveNonExemptDataAsync(
        CalculationInput input, CancellationToken ct)
    {
        if (input.FlsaStatusCode != "NON_EXEMPT") return null;

        var entries = await _timeEntryRepo.GetApprovedHoursByEmploymentAndPeriodAsync(
            input.EmploymentId, input.PayPeriodStart, input.PayPeriodEnd);

        var (regHours, otHours) = ComputeOtSplit(entries, input.OtWeeklyThresholdHours, input.WorkWeekStartDay);

        // base_rate is always the operative hourly rate — set at hire/comp-change time.
        // For salaried non-exempt employees it is pre-computed as annual_equivalent ÷ 2080.
        var hourlyRate = input.BaseRate;

        _logger.LogDebug(
            "NON_EXEMPT hours for employment {EmploymentId}: reg={RegHours:F4} ot={OtHours:F4} rate={Rate:F4}",
            input.EmploymentId, regHours, otHours, hourlyRate);

        return new NonExemptPayData(regHours, otHours, hourlyRate);
    }

    private static (decimal RegHours, decimal OtHours) ComputeOtSplit(
        IReadOnlyList<(DateOnly WorkDate, decimal Hours)> entries, decimal otThreshold, int weekStartDay)
    {
        var regHours = 0m;
        var otHours  = 0m;

        // Group by FLSA workweek using the context-configured anchor day
        foreach (var weekTotal in entries
                     .GroupBy(e => GetWeekStart(e.WorkDate, weekStartDay))
                     .Select(g => g.Sum(e => e.Hours)))
        {
            regHours += Math.Min(weekTotal, otThreshold);
            otHours  += Math.Max(weekTotal - otThreshold, 0m);
        }

        return (regHours, otHours);
    }

    // Returns the date of the anchor day that starts the FLSA workweek containing the given date.
    // weekStartDay: 0=Sunday, 1=Monday, … 6=Saturday (matches DayOfWeek integer values).
    private static DateOnly GetWeekStart(DateOnly date, int weekStartDay)
    {
        var dow  = (int)date.DayOfWeek;
        var diff = ((dow - weekStartDay) + 7) % 7;
        return date.AddDays(-diff);
    }

    private Task<IReadOnlyList<EarningsResultLine>> StepBaseEarningsAsync(
        CalculationInput input, Guid resultId, NonExemptPayData? nonExempt, CancellationToken ct)
    {
        var now   = DateTimeOffset.UtcNow;
        var lines = new List<EarningsResultLine>();

        if (nonExempt is not null)
        {
            // NON_EXEMPT: hours-based pay at the employee's hourly rate
            if (nonExempt.HourlyRate > 0m && (nonExempt.RegHours > 0m || nonExempt.OtHours > 0m))
            {
                if (nonExempt.RegHours > 0m)
                    lines.Add(MakeEarningsLine(resultId, input.EmploymentId, "REG", "Regular",
                        nonExempt.RegHours, nonExempt.HourlyRate,
                        Math.Round(nonExempt.RegHours * nonExempt.HourlyRate, 4, MidpointRounding.AwayFromZero),
                        now));

                if (nonExempt.OtHours > 0m)
                    lines.Add(MakeEarningsLine(resultId, input.EmploymentId, "OT", "Overtime",
                        nonExempt.OtHours, nonExempt.HourlyRate,
                        Math.Round(nonExempt.OtHours * nonExempt.HourlyRate, 4, MidpointRounding.AwayFromZero),
                        now));
            }
        }
        else
        {
            // SALARY + EXEMPT: fixed period amount; time entries irrelevant
            if (input.AnnualEquivalent is not null and not 0m && input.PeriodsPerYear > 0)
            {
                var amount = Math.Round(input.AnnualEquivalent.Value / input.PeriodsPerYear, 4,
                                 MidpointRounding.AwayFromZero);
                lines.Add(MakeEarningsLine(resultId, input.EmploymentId, "REG", "Regular Salary",
                    null, input.AnnualEquivalent.Value, amount, now));
            }
        }

        return Task.FromResult<IReadOnlyList<EarningsResultLine>>(lines);
    }

    private Task<IReadOnlyList<EarningsResultLine>> StepPremiumEarningsAsync(
        CalculationInput input, Guid resultId, NonExemptPayData? nonExempt, CancellationToken ct)
    {
        if (nonExempt is null || nonExempt.OtHours <= 0m || nonExempt.HourlyRate <= 0m)
            return Task.FromResult<IReadOnlyList<EarningsResultLine>>([]);

        // OT premium: 0.5× rate × OT hours (the extra half-time above straight pay)
        var premAmount = Math.Round(nonExempt.OtHours * nonExempt.HourlyRate * 0.5m, 4,
                             MidpointRounding.AwayFromZero);

        IReadOnlyList<EarningsResultLine> lines =
        [
            MakeEarningsLine(resultId, input.EmploymentId, "OT_PREM", "Overtime Premium",
                nonExempt.OtHours, nonExempt.HourlyRate * 0.5m, premAmount, DateTimeOffset.UtcNow)
        ];
        return Task.FromResult(lines);
    }

    private static EarningsResultLine MakeEarningsLine(
        Guid resultId, Guid employmentId, string code, string description,
        decimal? quantity, decimal rate, decimal amount, DateTimeOffset now) => new()
    {
        EarningsResultLineId    = Guid.NewGuid(),
        EmployeePayrollResultId = resultId,
        EmploymentId            = employmentId,
        EarningsCode            = code,
        EarningsDescription     = description,
        Quantity                = quantity,
        Rate                    = rate,
        CalculatedAmount        = amount,
        JurisdictionSplitFlag   = false,
        TaxableFlag             = true,
        AccumulatorImpactFlag   = true,
        SourceRuleVersionId     = null,
        CorrectionFlag          = false,
        CorrectsLineId          = null,
        CreationTimestamp       = now
    };

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
                // BOTH-step codes (e.g. US_FED_SS, US_FED_MEDICARE) are already accumulated
                // via the employee tax_result_line. Suppress here to avoid double-counting.
                AccumulatorImpactFlag            = !benefitCtx.BothStepCodes.Contains(code),
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
        CalculationInput input, Guid resultId, decimal taxableWages, decimal ficaTaxableWages, CancellationToken ct)
    {
        var jurisdictions = await _jurisdictionLookup.GetJurisdictionsAsync(
            input.EmploymentId, input.PayDate, ct);

        if (jurisdictions.Count == 0) return [];

        var ytdBalances = await _accumulatorService.GetYtdBalancesAsync(input.EmploymentId, input.PayDate);

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
                FicaTaxableWages  = ficaTaxableWages,
                PayPeriodsPerYear = input.PeriodsPerYear,
                JurisdictionCode  = jur.JurisdictionCode,
                YtdBalances       = ytdBalances,
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
                    // Employer-side tax lines share the same code as the employee side (e.g. US_FED_SS).
                    // There are no employer-side accumulator definitions yet, so suppress to prevent
                    // double-counting against the employee accumulator. Re-enable when US_FED_SS_ER
                    // definitions and distinct ER codes are introduced.
                    AccumulatorImpactFlag   = false,
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
