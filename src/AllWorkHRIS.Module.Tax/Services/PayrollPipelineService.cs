using System.Collections.Immutable;
using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Module.Tax.Queries;
using AllWorkHRIS.Module.Tax.Repositories;
using AllWorkHRIS.Module.Tax.Steps;
using Microsoft.Extensions.Logging;

namespace AllWorkHRIS.Module.Tax.Services;

public sealed class PayrollPipelineService : IPayrollPipelineService
{
    private readonly ITaxRateRepository          _rateRepo;
    private readonly ITaxFormSubmissionRepository _formRepo;
    private readonly IEnumerable<IBenefitStepProvider> _benefitProviders;
    private readonly ILogger<PayrollPipelineService>   _logger;

    public PayrollPipelineService(
        ITaxRateRepository           rateRepo,
        ITaxFormSubmissionRepository  formRepo,
        IEnumerable<IBenefitStepProvider> benefitProviders,
        ILogger<PayrollPipelineService>   logger)
    {
        _rateRepo         = rateRepo;
        _formRepo         = formRepo;
        _benefitProviders = benefitProviders;
        _logger           = logger;
    }

    public async Task<PipelineResult> RunAsync(PipelineRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Pipeline start — employment={EmploymentId} jurisdiction={JurisdictionCode} payDate={PayDate} gross={GrossPayPeriod}",
            request.EmploymentId, request.JurisdictionCode, request.PayDate, request.GrossPayPeriod);

        try
        {
            var profile = await _formRepo.GetActiveProfileAsync(
                request.EmploymentId, request.JurisdictionCode, request.PayDate, ct);

            var ctx = BuildInitialContext(request, profile);

            _logger.LogInformation(
                "Profile resolved — profileFound={ProfileFound} filingStatus={FilingStatus} exempt={Exempt}",
                profile is not null, ctx.FilingStatusCode ?? "(none)", ctx.ExemptFlag);

            var stepRows = await _rateRepo.GetActiveStepsAsync(
                request.JurisdictionCode, request.PayDate, ct);

            _logger.LogInformation("Active steps loaded: {Count} for jurisdiction {JurisdictionCode}",
                stepRows.Count, request.JurisdictionCode);

            var jurisdictionSteps = await BuildStepsAsync(stepRows, ctx, ct);

            var benefitSteps = new List<ICalculationStep>();
            if (!request.SkipBenefitSteps)
            {
                foreach (var provider in _benefitProviders)
                {
                    var providerSteps = await provider.GetStepsForEmployeeAsync(request, ct);
                    benefitSteps.AddRange(providerSteps);
                }
            }

            // Split at sequence 800: pre-tax benefits (100–199) + tax (200–799) run first,
            // then DisposableIncome is captured, then post-tax deductions (800+) run.
            var allSteps = jurisdictionSteps
                .Concat(benefitSteps)
                .OrderBy(s => s.SequenceNumber)
                .ToList();

            _logger.LogInformation("Executing {Count} steps", allSteps.Count);

            foreach (var step in allSteps.Where(s => s.SequenceNumber < 800))
            {
                ct.ThrowIfCancellationRequested();
                ctx = await step.ExecuteAsync(ctx, ct);
                _logger.LogDebug("Step {StepCode} → {Amount:N4}", step.StepCode,
                    ctx.StepResults.TryGetValue(step.StepCode, out var v) ? v : 0m);
            }

            // Disposable income is net pay after all taxes and pre-tax deductions,
            // before post-tax deductions and garnishments.
            ctx = ctx.WithDisposableIncome(ctx.NetPay);

            foreach (var step in allSteps.Where(s => s.SequenceNumber >= 800))
            {
                ct.ThrowIfCancellationRequested();
                ctx = await step.ExecuteAsync(ctx, ct);
                _logger.LogDebug("Step {StepCode} → {Amount:N4}", step.StepCode,
                    ctx.StepResults.TryGetValue(step.StepCode, out var v) ? v : 0m);
            }

            var employeeSteps = ctx.StepResults
                .Where(kv => !ctx.EmployerStepResults.ContainsKey(kv.Key)
                          || ctx.BothStepCodes.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            _logger.LogInformation(
                "Pipeline complete — computedTax={ComputedTax:N4} netPay={NetPay:N4} employeeSteps={EmpSteps} employerSteps={ErSteps}",
                ctx.ComputedTax, ctx.NetPay, employeeSteps.Count, ctx.EmployerStepResults.Count);

            return new PipelineResult
            {
                Succeeded           = true,
                GrossPayPeriod      = request.GrossPayPeriod,
                NetPay              = ctx.NetPay,
                ComputedTax         = ctx.ComputedTax,
                EmployerCost        = ctx.EmployerCost,
                StepResults         = employeeSteps,
                EmployerStepResults = ctx.EmployerStepResults
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Pipeline failed — employment={EmploymentId} jurisdiction={JurisdictionCode}",
                request.EmploymentId, request.JurisdictionCode);

            return new PipelineResult
            {
                Succeeded      = false,
                FailureReason  = ex.Message,
                GrossPayPeriod = request.GrossPayPeriod,
                NetPay         = request.GrossPayPeriod,
                ComputedTax    = 0m,
                EmployerCost   = 0m
            };
        }
    }

    private static CalculationContext BuildInitialContext(
        PipelineRequest request, EmployeeFilingProfile? profile)
    {
        var ytd = ImmutableDictionary.CreateRange(
            request.YtdBalances.Select(kv => KeyValuePair.Create(kv.Key, kv.Value)));

        return new CalculationContext
        {
            EmployeeId        = request.EmployeeId,
            PayrollContextId  = request.PayrollContextId,
            PeriodId          = request.PeriodId,
            PayDate           = request.PayDate,
            PayPeriodsPerYear = request.PayPeriodsPerYear,
            JurisdictionCode  = request.JurisdictionCode,

            PayPeriodStart         = request.PayPeriodStart,
            PayPeriodEnd           = request.PayPeriodEnd,
            PayDatesInPeriodMonth  = request.PayDatesInPeriodMonth,
            PayDateOrdinalInMonth  = request.PayDateOrdinalInMonth,
            PartialPeriodRule      = request.PartialPeriodRule,
            ThreePaycheckMonthRule = request.ThreePaycheckMonthRule,

            GrossPayPeriod    = request.GrossPayPeriod,
            AnnualizedGross   = request.GrossPayPeriod * request.PayPeriodsPerYear,
            IncomeTaxableWages = request.GrossPayPeriod,
            FicaTaxableWages   = request.GrossPayPeriod,
            DisposableIncome   = 0m,

            ComputedTax  = 0m,
            NetPay       = request.GrossPayPeriod,
            EmployerCost = 0m,

            YtdBalances = ytd,

            FilingStatusCode      = profile?.FilingStatusCode ?? request.FilingStatusCode,
            AllowanceCount        = profile?.AllowanceCount        ?? 0,
            AdditionalWithholding = profile?.AdditionalWithholding ?? 0m,
            ExemptFlag            = profile?.ExemptFlag            ?? false,
            IsLegacyForm          = profile?.IsLegacyForm          ?? false,
            OtherIncomeAmount     = profile?.OtherIncomeAmount     ?? 0m,
            DeductionsAmount      = profile?.DeductionsAmount      ?? 0m,
            CreditsAmount         = profile?.CreditsAmount         ?? 0m,
            ClaimCode             = profile?.ClaimCode,
            TotalClaimAmount      = profile?.TotalClaimAmount      ?? 0m
        };
    }

    private async Task<IReadOnlyList<ICalculationStep>> BuildStepsAsync(
        IReadOnlyList<CalculationStepRow> stepRows, CalculationContext ctx, CancellationToken ct)
    {
        var steps = new List<ICalculationStep>(stepRows.Count);

        foreach (var row in stepRows)
        {
            var appliesTo = row.AppliesTo switch
            {
                "EMPLOYER" => StepAppliesTo.Employer,
                "BOTH"     => StepAppliesTo.Both,
                _          => StepAppliesTo.Employee
            };

            var useFica = row.CalculationCategory == "SOCIAL_INSURANCE"
                       || row.CalculationCategory == "EMPLOYER_CONTRIBUTION";

            ICalculationStep? step = row.StepType switch
            {
                "STANDARD_DEDUCTION"  => await BuildStandardDeductionAsync(row, ctx.PayDate, ctx.FilingStatusCode, ct),
                "ALLOWANCE"           => await BuildAllowanceAsync(row, ctx.PayDate, ctx.FilingStatusCode, ct),
                "PROGRESSIVE_BRACKET" => await BuildProgressiveBracketAsync(row, ctx.PayDate, ctx.FilingStatusCode, ct),
                "CREDIT"              => await BuildCreditAsync(row, ctx.PayDate, ct),
                "FLAT_RATE"           => await BuildFlatRateAsync(row, ctx.PayDate, appliesTo, useFica, ct),
                "TIERED_FLAT"         => await BuildTieredFlatAsync(row, ctx.PayDate, appliesTo, ct),
                "PERCENTAGE_OF_PRIOR" => await BuildPercentageOfPriorAsync(row, ctx.PayDate, ct),
                _                     => null
            };

            if (step is not null)
                steps.Add(step);
            else
                _logger.LogWarning("Step skipped — no rate data found for {StepCode} ({StepType}) payDate={PayDate} filingStatus={FilingStatus}",
                    row.StepCode, row.StepType, ctx.PayDate, ctx.FilingStatusCode ?? "(none)");
        }

        return steps;
    }

    private async Task<ICalculationStep?> BuildStandardDeductionAsync(
        CalculationStepRow row, DateOnly payDate, string? filingStatus, CancellationToken ct)
    {
        var data = await _rateRepo.GetAllowanceAsync(row.StepCode, filingStatus, payDate, ct);
        return data is null ? null
            : new StandardDeductionStep(row.StepCode, row.SequenceNumber, data.AnnualAmount);
    }

    private async Task<ICalculationStep?> BuildAllowanceAsync(
        CalculationStepRow row, DateOnly payDate, string? filingStatus, CancellationToken ct)
    {
        var data = await _rateRepo.GetAllowanceAsync(row.StepCode, filingStatus, payDate, ct);
        return data is null ? null
            : new AllowanceStep(row.StepCode, row.SequenceNumber, data.AnnualAmount);
    }

    private async Task<ICalculationStep?> BuildProgressiveBracketAsync(
        CalculationStepRow row, DateOnly payDate, string? filingStatus, CancellationToken ct)
    {
        var brackets = await _rateRepo.GetBracketsAsync(row.StepCode, filingStatus, payDate, ct);
        if (brackets.Count == 0)
        {
            _logger.LogWarning("No brackets found — step={StepCode} filingStatus={FilingStatus} payDate={PayDate}",
                row.StepCode, filingStatus ?? "(none)", payDate);
            return null;
        }
        return new ProgressiveBracketStep(row.StepCode, row.SequenceNumber, brackets);
    }

    private async Task<ICalculationStep?> BuildCreditAsync(
        CalculationStepRow row, DateOnly payDate, CancellationToken ct)
    {
        var data = await _rateRepo.GetCreditAsync(row.StepCode, payDate, ct);
        return data is null ? null
            : new CreditStep(row.StepCode, row.SequenceNumber, data.AnnualAmount, data.CreditRate);
    }

    private async Task<ICalculationStep?> BuildFlatRateAsync(
        CalculationStepRow row, DateOnly payDate, StepAppliesTo appliesTo,
        bool useFica, CancellationToken ct)
    {
        var data = await _rateRepo.GetFlatRateAsync(row.StepCode, payDate, ct);
        return data is null ? null
            : new FlatRateStep(row.StepCode, row.SequenceNumber, appliesTo,
                data.Rate, data.WageBase, data.PeriodCap, data.AnnualCap, useFica);
    }

    private async Task<ICalculationStep?> BuildTieredFlatAsync(
        CalculationStepRow row, DateOnly payDate, StepAppliesTo appliesTo, CancellationToken ct)
    {
        var tiers = await _rateRepo.GetTieredBracketsAsync(row.StepCode, payDate, ct);
        return tiers.Count == 0 ? null
            : new TieredFlatStep(row.StepCode, row.SequenceNumber, appliesTo, tiers);
    }

    private async Task<ICalculationStep?> BuildPercentageOfPriorAsync(
        CalculationStepRow row, DateOnly payDate, CancellationToken ct)
    {
        var data = await _rateRepo.GetFlatRateAsync(row.StepCode, payDate, ct);
        if (data is null || data.DependsOnStepCode is null) return null;
        return new PercentageOfPriorResultStep(
            row.StepCode, row.SequenceNumber, data.DependsOnStepCode, data.Rate);
    }
}
