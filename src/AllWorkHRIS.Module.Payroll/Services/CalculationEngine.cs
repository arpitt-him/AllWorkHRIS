using Microsoft.Extensions.Logging;
using AllWorkHRIS.Module.Payroll.Domain.Results;
using AllWorkHRIS.Module.Payroll.Repositories;

namespace AllWorkHRIS.Module.Payroll.Services;

public sealed partial class CalculationEngine : ICalculationEngine
{
    private readonly IResultLineRepository _resultLineRepo;
    private readonly ILogger<CalculationEngine> _logger;

    public CalculationEngine(IResultLineRepository resultLineRepo, ILogger<CalculationEngine> logger)
    {
        _resultLineRepo = resultLineRepo;
        _logger         = logger;
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

            // Step 3 — Pre-tax deductions (reduce taxable wage base)
            var preTaxDeductions = await StepPreTaxDeductionsAsync(input, employeePayrollResultId, ct);
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
            var taxableWages = grossPay - preTaxTotal;

            ct.ThrowIfCancellationRequested();

            // Step 6 — Tax withholdings (federal, state, local)
            var taxLines = await StepTaxWithholdingsAsync(input, employeePayrollResultId, taxableWages, ct);
            foreach (var line in taxLines)
                await _resultLineRepo.InsertTaxLineAsync(line);

            ct.ThrowIfCancellationRequested();

            // Step 7 — Post-tax deductions
            var postTaxDeductions = await StepPostTaxDeductionsAsync(input, employeePayrollResultId, ct);
            foreach (var line in postTaxDeductions)
                await _resultLineRepo.InsertDeductionLineAsync(line);

            ct.ThrowIfCancellationRequested();

            // Step 8 — Employer contributions
            var employerContributions = await StepEmployerContributionsAsync(input, employeePayrollResultId, ct);
            foreach (var line in employerContributions)
                await _resultLineRepo.InsertEmployerContributionLineAsync(line);

            // Step 9 — Net pay
            var totalDeductions  = preTaxDeductions.Concat(postTaxDeductions).Sum(l => l.CalculatedAmount);
            var totalTax         = taxLines.Sum(l => l.CalculatedAmount);
            var totalContribs    = employerContributions.Sum(l => l.CalculatedAmount);
            var netPay           = grossPay - totalDeductions - totalTax;

            LogCalculationComplete(input.EmploymentId, grossPay, netPay);

            return new CalculationOutput
            {
                EmployeePayrollResultId    = employeePayrollResultId,
                Succeeded                  = true,
                GrossPay                   = grossPay,
                TotalDeductionsAmount      = totalDeductions,
                TotalEmployeeTaxAmount     = totalTax,
                TotalEmployerContribAmount = totalContribs,
                NetPay                     = netPay
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

    // -------------------------------------------------------
    // Sub-calculator stubs — each step returns an empty list
    // until compensation rules, benefit elections, T&A data,
    // and jurisdiction tax tables are wired in (Phase 4.6+).
    // -------------------------------------------------------

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

    private Task<IReadOnlyList<DeductionResultLine>> StepPreTaxDeductionsAsync(
        CalculationInput input, Guid resultId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<DeductionResultLine>>([]);

    private Task<IReadOnlyList<EarningsResultLine>> StepImputedIncomeAsync(
        CalculationInput input, Guid resultId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<EarningsResultLine>>([]);

    private Task<IReadOnlyList<TaxResultLine>> StepTaxWithholdingsAsync(
        CalculationInput input, Guid resultId, decimal taxableWages, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<TaxResultLine>>([]);

    private Task<IReadOnlyList<DeductionResultLine>> StepPostTaxDeductionsAsync(
        CalculationInput input, Guid resultId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<DeductionResultLine>>([]);

    private Task<IReadOnlyList<EmployerContributionResultLine>> StepEmployerContributionsAsync(
        CalculationInput input, Guid resultId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<EmployerContributionResultLine>>([]);
}
