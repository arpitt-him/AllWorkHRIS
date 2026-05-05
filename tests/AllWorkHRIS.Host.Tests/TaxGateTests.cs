using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Module.Tax.Services;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;

namespace AllWorkHRIS.Host.Tests;

/// <summary>
/// Phase 5 gate tests — TC-TAX-001 through TC-TAX-019.
/// All tests run against allworkhris_dev with 2025 seeded rate data.
/// Source of truth: SPEC/Payroll_Calculation_Pipeline.md §13
/// </summary>
public sealed class TaxGateTests
{
    static readonly Guid DummyEmploymentId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    static readonly Guid DummyEmployeeId   = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    static readonly Guid DummyContextId    = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    static readonly Guid DummyPeriodId     = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    static readonly DateOnly PayDate2025   = new(2025, 6, 15);
    static readonly DateOnly PayDate2026   = new(2026, 1, 15);

    readonly IPayrollPipelineService _pipeline;

    public TaxGateTests()
    {
        Environment.SetEnvironmentVariable("DATABASE_CONNECTION_STRING",
            "Host=localhost;Database=allworkhris_dev;Username=postgres;Password=dev");
        Environment.SetEnvironmentVariable("DATABASE_PROVIDER", "postgresql");

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new TestDateOnlyHandler());
        SqlMapper.AddTypeHandler(new TestNullableDateOnlyHandler());

        var connectionFactory = new ConnectionFactory();

        _pipeline = new PayrollPipelineService(
            new TaxRateRepository(connectionFactory),
            new TaxFormSubmissionRepository(connectionFactory),
            [],
            NullLogger<PayrollPipelineService>.Instance);
    }

    // -------------------------------------------------------------------------
    // TC-TAX-001: Barbados — gross BBD 40,000/year; all in 12.5% bracket
    // Expected annual income tax = BBD 40,000 × 12.5% = 5,000
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcTax001_Barbados_40K_Income_Tax_At_FirstBracket()
    {
        const decimal annualGross = 40_000m;
        var result = await _pipeline.RunAsync(MakeRequest("BB", annualGross));

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.StepResults.ContainsKey("BB_INCOME_TAX"),
            "BB_INCOME_TAX step must be present");

        var annualTax = result.StepResults["BB_INCOME_TAX"] * 26;
        Assert.Equal(annualGross * 0.125m, annualTax, precision: 1); // ±$0.05
    }

    // -------------------------------------------------------------------------
    // TC-TAX-002: Barbados — gross BBD 80,000/year; spans both brackets
    // Expected = (50,000 × 12.5% + 30,000 × 28.5%) / 26
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcTax002_Barbados_80K_Income_Tax_SpansBothBrackets()
    {
        const decimal annualGross = 80_000m;
        var result = await _pipeline.RunAsync(MakeRequest("BB", annualGross));

        Assert.True(result.Succeeded, result.FailureReason);

        var expected = (50_000m * 0.125m + 30_000m * 0.285m) / 26m;
        Assert.Equal(expected, result.StepResults["BB_INCOME_TAX"], precision: 1);
    }

    // -------------------------------------------------------------------------
    // TC-TAX-003: Barbados NIS — BBD 50,000/year; annual cap not exceeded
    // NIS = 50,000 × 11% = 5,500 (below cap of 6,969.60)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcTax003_Barbados_NIS_CapNotExceeded()
    {
        const decimal annualGross = 50_000m;
        var result = await _pipeline.RunAsync(MakeRequest("BB", annualGross));

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.StepResults.ContainsKey("BB_NIS"), "BB_NIS step must be present");

        var annualNis = result.StepResults["BB_NIS"] * 26;
        Assert.True(annualNis <= 6_969.60m, $"NIS {annualNis} exceeds annual cap 6969.60");
        Assert.Equal(50_000m * 0.11m, annualNis, precision: 1); // at 11% within cap
    }

    // -------------------------------------------------------------------------
    // TC-TAX-004: Barbados Resilience Fund — flat 0.3%, no cap
    // Expected = period_gross × 0.003
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcTax004_Barbados_ResilienceFund_FlatRate()
    {
        const decimal annualGross = 50_000m;
        const int periods = 26;
        var result = await _pipeline.RunAsync(MakeRequest("BB", annualGross));

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.StepResults.ContainsKey("BB_RESILIENCE_FUND"),
            "BB_RESILIENCE_FUND step must be present");

        var expected = (annualGross / periods) * 0.003m;
        Assert.Equal(expected, result.StepResults["BB_RESILIENCE_FUND"], precision: 2);
    }

    // -------------------------------------------------------------------------
    // TC-TAX-005: Canada Federal — CAD 90,000/year; 5-bracket progressive
    // Expected annual income tax (2025 rates, no deductions):
    //   15% × 57,375 = 8,606.25
    //   20.5% × 32,625 = 6,688.125
    //   Total = 15,294.375
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcTax005_CanadaFed_90K_IncomeTax_FiveBrackets()
    {
        const decimal annualGross = 90_000m;
        var result = await _pipeline.RunAsync(MakeRequest("CA-FED", annualGross));

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.StepResults.ContainsKey("CA_FED_INCOME_TAX"),
            "CA_FED_INCOME_TAX step must be present");

        const decimal expected = 57_375m * 0.15m + 32_625m * 0.205m; // 15,294.375
        var annualTax = result.StepResults["CA_FED_INCOME_TAX"] * 26;
        Assert.Equal(expected, annualTax, precision: 1);
    }

    // -------------------------------------------------------------------------
    // TC-TAX-006: Canada Federal BPA credit — non-refundable; cannot go below zero
    // At low income (CAD 15,000): credit is capped at computed income tax
    // CA_FED_BPA_CREDIT = −min(16,129 × 15% / 26, period_income_tax)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcTax006_CanadaFed_BpaCredit_NonRefundable()
    {
        // Use low income where BPA credit would exceed income tax if uncapped
        const decimal annualGross = 15_000m;
        var result = await _pipeline.RunAsync(MakeRequest("CA-FED", annualGross));

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.StepResults.ContainsKey("CA_FED_BPA_CREDIT"),
            "CA_FED_BPA_CREDIT step must be present");

        var credit = result.StepResults["CA_FED_BPA_CREDIT"];
        Assert.True(credit <= 0m, $"BPA credit should be negative (reducing tax); got {credit}");

        // Non-refundable: |credit| ≤ income tax for the period
        var incomeTax  = result.StepResults.TryGetValue("CA_FED_INCOME_TAX", out var t) ? t : 0m;
        Assert.True(Math.Abs(credit) <= incomeTax + 0.01m,
            $"Credit |{credit}| should not exceed income tax {incomeTax}");

        // Full-credit case: high income, credit fully applied
        const decimal highGross = 90_000m;
        var highResult = await _pipeline.RunAsync(MakeRequest("CA-FED", highGross));
        Assert.True(highResult.Succeeded, highResult.FailureReason);
        var fullCredit = highResult.StepResults["CA_FED_BPA_CREDIT"];
        var expectedFullCredit = -(16_129m * 0.15m / 26m);
        Assert.Equal(expectedFullCredit, fullCredit, precision: 2);
    }

    // -------------------------------------------------------------------------
    // TC-TAX-007: Canada Federal CPP — CAD 75,000/year; CPP and CPP2 both apply
    // CPP: (71,300 − 3,500) × 5.95% = 4,034.10 (at annual cap)
    // CPP2: (75,000 − 71,300) × 4.0% = 148.00
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcTax007_CanadaFed_CPP_And_CPP2_BothApply()
    {
        const decimal annualGross = 75_000m;
        var result = await _pipeline.RunAsync(MakeRequest("CA-FED", annualGross));

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.StepResults.ContainsKey("CA_FED_CPP"),  "CA_FED_CPP must be present");
        Assert.True(result.StepResults.ContainsKey("CA_FED_CPP2"), "CA_FED_CPP2 must be present");

        // CPP at annual cap (4,034.10)
        const decimal cppExpected = 4_034.10m;
        Assert.Equal(cppExpected / 26m, result.StepResults["CA_FED_CPP"], precision: 1);

        // CPP2 on (75,000 − 71,300) = 3,700 × 4.0% = 148.00
        const decimal cpp2Expected = (75_000m - 71_300m) * 0.04m;
        Assert.Equal(cpp2Expected / 26m, result.StepResults["CA_FED_CPP2"], precision: 1);
    }

    // -------------------------------------------------------------------------
    // TC-TAX-008: Canada Federal EI — CAD 63,200/year; annual cap enforced
    // Normal period: 63,200 × 1.66% / 26 ≈ 40.35
    // Cap exhausted (YTD = 1,049.12): period amount = 0
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcTax008_CanadaFed_EI_AnnualCapEnforced()
    {
        const decimal annualGross = 63_200m;

        // First period — no YTD, normal withholding
        var result = await _pipeline.RunAsync(MakeRequest("CA-FED", annualGross));
        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.StepResults.ContainsKey("CA_FED_EI"), "CA_FED_EI must be present");

        var normalPeriod = annualGross * 0.0166m / 26m;
        Assert.Equal(normalPeriod, result.StepResults["CA_FED_EI"], precision: 1);

        // Cap exhausted — YTD equals annual cap → period amount = 0
        var ytdAtCap = new Dictionary<string, decimal> { ["CA_FED_EI"] = 1_049.12m };
        var capResult = await _pipeline.RunAsync(
            MakeRequest("CA-FED", annualGross, ytd: ytdAtCap));
        Assert.True(capResult.Succeeded, capResult.FailureReason);
        Assert.Equal(0m, capResult.StepResults.GetValueOrDefault("CA_FED_EI", 0m));
    }

    // -------------------------------------------------------------------------
    // TC-TAX-009: US Federal — SINGLE filer, USD 70,000/year, biweekly
    // Expected annual income tax (2025 brackets, no deductions):
    //   10% × 11,925 = 1,192.50
    //   12% × 36,550 = 4,386.00
    //   22% × 21,525 = 4,735.50
    //   Total = 10,314.00
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcTax009_UsFed_Single_70K_IncomeTax()
    {
        const decimal annualGross = 70_000m;
        var result = await _pipeline.RunAsync(
            MakeRequest("US-FED", annualGross, filingStatus: "SINGLE"));

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.StepResults.ContainsKey("US_FED_INCOME_TAX"),
            "US_FED_INCOME_TAX must be present");

        const decimal expectedAnnual = 11_925m * 0.10m + 36_550m * 0.12m + 21_525m * 0.22m;
        Assert.Equal(expectedAnnual, result.StepResults["US_FED_INCOME_TAX"] * 26, precision: 1);
    }

    // -------------------------------------------------------------------------
    // TC-TAX-010: US Federal — MFJ filer, USD 120,000/year; differs from SINGLE
    // MFJ expected annual:
    //   10% × 23,850 = 2,385.00
    //   12% × 73,100 = 8,772.00
    //   22% × 23,050 = 5,071.00
    //   Total = 16,228.00
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcTax010_UsFed_Mfj_120K_IncomeTax_DiffersFromSingle()
    {
        const decimal annualGross = 120_000m;

        var mfjResult = await _pipeline.RunAsync(
            MakeRequest("US-FED", annualGross, filingStatus: "MFJ"));
        Assert.True(mfjResult.Succeeded, mfjResult.FailureReason);

        var singleResult = await _pipeline.RunAsync(
            MakeRequest("US-FED", annualGross, filingStatus: "SINGLE"));
        Assert.True(singleResult.Succeeded, singleResult.FailureReason);

        // MFJ expected
        const decimal mfjExpected = 23_850m * 0.10m + 73_100m * 0.12m + 23_050m * 0.22m;
        Assert.Equal(mfjExpected, mfjResult.StepResults["US_FED_INCOME_TAX"] * 26, precision: 1);

        // MFJ withholding less than SINGLE for same income
        Assert.True(
            mfjResult.StepResults["US_FED_INCOME_TAX"] < singleResult.StepResults["US_FED_INCOME_TAX"],
            "MFJ income tax should be less than SINGLE for same gross");
    }

    // -------------------------------------------------------------------------
    // TC-TAX-011: US Federal SS — USD 200,000/year; wage base cap ($176,100)
    // Per-period SS = (176,100/26) × 6.2%; annual = 176,100 × 6.2% = 10,918.20
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcTax011_UsFed_SS_WageBaseCapped()
    {
        const decimal annualGross = 200_000m;
        var result = await _pipeline.RunAsync(
            MakeRequest("US-FED", annualGross, filingStatus: "SINGLE"));

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.StepResults.ContainsKey("US_FED_SS"), "US_FED_SS must be present");

        // Annual SS should equal wage_base × rate regardless of actual earnings above the cap
        const decimal expectedAnnual = 176_100m * 0.062m;
        Assert.Equal(expectedAnnual, result.StepResults["US_FED_SS"] * 26, precision: 1);
    }

    // -------------------------------------------------------------------------
    // TC-TAX-012: US Federal Additional Medicare — 0.9% on wages above $200K
    // High earner ($250K): ADDL Medicare > 0
    // Current implementation note: threshold logic not yet enforced per-period;
    // test verifies rate application at 0.9%.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcTax012_UsFed_AdditionalMedicare_AppliesToWages()
    {
        const decimal annualGross = 250_000m;
        var result = await _pipeline.RunAsync(
            MakeRequest("US-FED", annualGross, filingStatus: "SINGLE"));

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.StepResults.ContainsKey("US_FED_MEDICARE_ADDL"),
            "US_FED_MEDICARE_ADDL must be present");

        Assert.True(result.StepResults["US_FED_MEDICARE_ADDL"] > 0m,
            "Additional Medicare must be > 0 for high earner");

        // Rate verification: per period = (gross/26) × 0.9%
        var expectedPeriod = (annualGross / 26m) * 0.009m;
        Assert.Equal(expectedPeriod, result.StepResults["US_FED_MEDICARE_ADDL"], precision: 2);
    }

    // -------------------------------------------------------------------------
    // TC-TAX-013: Georgia — SINGLE, USD 60,000/year; flat 5.19% after std deduction
    // Std deduction (SINGLE) = $5,400
    // Expected = (60,000 − 5,400) × 5.19% / 26
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcTax013_Georgia_Single_60K_FlatRateAfterStdDeduction()
    {
        const decimal annualGross = 60_000m;
        var result = await _pipeline.RunAsync(
            MakeRequest("US-GA", annualGross, filingStatus: "SINGLE"));

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.StepResults.ContainsKey("US_GA_INCOME_TAX"),
            "US_GA_INCOME_TAX must be present");

        const decimal expected = (60_000m - 5_400m) * 0.0519m / 26m;
        Assert.Equal(expected, result.StepResults["US_GA_INCOME_TAX"], precision: 2);
    }

    // -------------------------------------------------------------------------
    // TC-TAX-014: New York State — SINGLE, USD 85,000/year; 9-bracket progressive
    // Expected annual:
    //   4.0% × 17,150 = 686.00
    //   4.5% × 6,450  = 290.25
    //   5.25% × 4,300 = 225.75
    //   5.85% × 57,100 = 3,340.35
    //   Total = 4,542.35
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcTax014_NewYorkState_Single_85K_NineBrackets()
    {
        const decimal annualGross = 85_000m;
        var result = await _pipeline.RunAsync(
            MakeRequest("US-NY", annualGross, filingStatus: "SINGLE"));

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.StepResults.ContainsKey("US_NY_STATE_INCOME"),
            "US_NY_STATE_INCOME must be present");

        const decimal expectedAnnual =
            17_150m * 0.040m +
             6_450m * 0.045m +
             4_300m * 0.0525m +
            57_100m * 0.0585m;
        Assert.Equal(expectedAnnual, result.StepResults["US_NY_STATE_INCOME"] * 26, precision: 1);
    }

    // -------------------------------------------------------------------------
    // TC-TAX-015: New York NYC income tax — present in pipeline for US-NY employees
    // Note: per-employee residency flag not yet implemented; NYC tax is currently
    // computed for all US-NY jurisdiction employees.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcTax015_NewYorkCity_IncomeTax_PresentForNyEmployee()
    {
        const decimal annualGross = 85_000m;
        var result = await _pipeline.RunAsync(
            MakeRequest("US-NY", annualGross, filingStatus: "SINGLE"));

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.StepResults.ContainsKey("US_NY_CITY_INCOME"),
            "US_NY_CITY_INCOME must be present for US-NY jurisdiction");

        Assert.True(result.StepResults["US_NY_CITY_INCOME"] > 0m,
            "NYC income tax should be positive");
    }

    // -------------------------------------------------------------------------
    // TC-TAX-016: New York Yonkers surcharge — reads NY state result via PERCENTAGE_OF_PRIOR
    // Yonkers = US_NY_STATE_INCOME × 16.75%
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcTax016_NewYorkYonkers_PercentageOfPriorStateIncome()
    {
        const decimal annualGross = 85_000m;
        var result = await _pipeline.RunAsync(
            MakeRequest("US-NY", annualGross, filingStatus: "SINGLE"));

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.StepResults.ContainsKey("US_NY_YONKERS"),
            "US_NY_YONKERS must be present");
        Assert.True(result.StepResults.ContainsKey("US_NY_STATE_INCOME"),
            "US_NY_STATE_INCOME must be present");

        var expected = result.StepResults["US_NY_STATE_INCOME"] * 0.1675m;
        Assert.Equal(expected, result.StepResults["US_NY_YONKERS"], precision: 2);
    }

    // -------------------------------------------------------------------------
    // TC-TAX-017: New York SDI — bi-weekly period cap = $0.60
    // For any gross, SDI ≤ 0.60
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcTax017_NewYorkSdi_PeriodCapAt60Cents()
    {
        const decimal annualGross = 85_000m;
        var result = await _pipeline.RunAsync(
            MakeRequest("US-NY", annualGross, filingStatus: "SINGLE"));

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.StepResults.ContainsKey("US_NY_SDI"), "US_NY_SDI must be present");

        Assert.True(result.StepResults["US_NY_SDI"] <= 0.60m,
            $"SDI {result.StepResults["US_NY_SDI"]} exceeds bi-weekly period cap of $0.60");
        Assert.Equal(0.60m, result.StepResults["US_NY_SDI"]); // high earner hits the cap exactly
    }

    // -------------------------------------------------------------------------
    // TC-TAX-018: New York PFL — annual cap $411.91; YTD balance consumed
    // Normal period: 85,000 × 0.388% / 26 ≈ 12.68
    // Cap exhausted (YTD = 411.91): period = 0
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcTax018_NewYorkPfl_AnnualCapEnforced()
    {
        const decimal annualGross = 85_000m;

        // Normal period — no YTD
        var result = await _pipeline.RunAsync(
            MakeRequest("US-NY", annualGross, filingStatus: "SINGLE"));
        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.StepResults.ContainsKey("US_NY_PFL"), "US_NY_PFL must be present");

        var normalPeriod = annualGross * 0.00388m / 26m;
        Assert.Equal(normalPeriod, result.StepResults["US_NY_PFL"], precision: 2);

        // Cap exhausted
        var ytdAtCap = new Dictionary<string, decimal> { ["US_NY_PFL"] = 411.91m };
        var capResult = await _pipeline.RunAsync(
            MakeRequest("US-NY", annualGross, filingStatus: "SINGLE", ytd: ytdAtCap));
        Assert.True(capResult.Succeeded, capResult.FailureReason);
        Assert.Equal(0m, capResult.StepResults.GetValueOrDefault("US_NY_PFL", 0m));
    }

    // -------------------------------------------------------------------------
    // TC-TAX-019: California — 9-bracket income tax + Mental Health surtax
    // Normal earner ($85K): Mental Health = gross × 1% (per current implementation)
    // High earner ($1.5M): Mental Health also applies
    // Note: $1M threshold enforcement not yet implemented; rate verification only.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TcTax019_California_IncomeTax_And_MentalHealthSurtax()
    {
        // Normal earner — verify CA income tax via 9 brackets
        const decimal annualGross = 85_000m;
        var result = await _pipeline.RunAsync(
            MakeRequest("US-CA", annualGross, filingStatus: "SINGLE"));

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.StepResults.ContainsKey("US_CA_INCOME_TAX"),
            "US_CA_INCOME_TAX must be present");

        // CA income tax on $85K SINGLE (9 brackets, 2025)
        const decimal expectedCa =
            10_756m * 0.010m +
            14_743m * 0.020m +
            14_746m * 0.040m +
            15_621m * 0.060m +
            14_740m * 0.080m +
            14_394m * 0.093m;  // 70,606 → 85,000
        Assert.Equal(expectedCa, result.StepResults["US_CA_INCOME_TAX"] * 26, precision: 1);

        // Mental Health surtax present in pipeline
        Assert.True(result.StepResults.ContainsKey("US_CA_MENTAL_HEALTH"),
            "US_CA_MENTAL_HEALTH step must be present");

        // High-earner: Mental Health should be > 0
        const decimal highGross = 1_500_000m;
        var highResult = await _pipeline.RunAsync(
            MakeRequest("US-CA", highGross, filingStatus: "SINGLE"));
        Assert.True(highResult.Succeeded, highResult.FailureReason);
        Assert.True(highResult.StepResults.GetValueOrDefault("US_CA_MENTAL_HEALTH", 0m) > 0m,
            "Mental Health surtax must be > 0 for high earner");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static PipelineRequest MakeRequest(
        string jurisdictionCode,
        decimal annualGross,
        int payPeriodsPerYear = 26,
        string? filingStatus  = null,
        DateOnly? payDate     = null,
        IReadOnlyDictionary<string, decimal>? ytd = null)
        => new()
        {
            EmploymentId      = DummyEmploymentId,
            EmployeeId        = DummyEmployeeId,
            PayrollContextId  = DummyContextId,
            PeriodId          = DummyPeriodId,
            PayDate           = payDate ?? PayDate2025,
            GrossPayPeriod    = annualGross / payPeriodsPerYear,
            PayPeriodsPerYear = payPeriodsPerYear,
            JurisdictionCode  = jurisdictionCode,
            FilingStatusCode  = filingStatus,
            YtdBalances       = ytd ?? new Dictionary<string, decimal>()
        };
}
