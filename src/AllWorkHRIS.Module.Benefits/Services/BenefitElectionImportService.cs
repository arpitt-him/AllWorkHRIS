using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Benefits.Commands;
using AllWorkHRIS.Module.Benefits.Domain.Codes;
using AllWorkHRIS.Module.Benefits.Queries;
using AllWorkHRIS.Module.Benefits.Repositories;
using Dapper;
using Microsoft.Extensions.Logging;

namespace AllWorkHRIS.Module.Benefits.Services;

public sealed class BenefitElectionImportService : IBenefitElectionImportService
{
    private readonly IBenefitElectionService                 _electionService;
    private readonly IDeductionRepository                    _deductionRepository;
    private readonly IConnectionFactory                      _connectionFactory;
    private readonly ILogger<BenefitElectionImportService>   _logger;

    private static readonly HashSet<string> ValidCoverageTiers =
        ["EE_ONLY", "EE_SPOUSE", "EE_CHILD", "FAMILY"];

    public BenefitElectionImportService(
        IBenefitElectionService                 electionService,
        IDeductionRepository                    deductionRepository,
        IConnectionFactory                      connectionFactory,
        ILogger<BenefitElectionImportService>   logger)
    {
        _electionService     = electionService;
        _deductionRepository = deductionRepository;
        _connectionFactory   = connectionFactory;
        _logger              = logger;
    }

    // ── Validate ─────────────────────────────────────────────────────────

    public async Task<BatchValidationResult> ValidateBatchAsync(
        Stream fileContent, string fileFormat, CancellationToken ct = default)
    {
        var (records, totalDataRows, parseErrors) = await ParseCsvAsync(fileContent, ct);
        var errors = new List<RecordError>(parseErrors);

        var deductionMap = (await _deductionRepository.GetActiveCodesAsync(ct))
            .ToDictionary(d => d.Code, StringComparer.OrdinalIgnoreCase);

        var employeeNumbers = records.Select(r => r.EmployeeNumber).Distinct().ToArray();
        var resolved        = await ResolveEmployeeNumbersAsync(employeeNumbers, ct);

        foreach (var row in records)
        {
            if (!resolved.ContainsKey(row.EmployeeNumber))
                errors.Add(new RecordError
                {
                    RowNumber    = row.RowNumber,
                    Field        = "employee_number",
                    ErrorMessage = $"Employee '{row.EmployeeNumber}' not found in active employment records."
                });

            if (!deductionMap.TryGetValue(row.DeductionCode, out var deduction))
            {
                errors.Add(new RecordError
                {
                    RowNumber    = row.RowNumber,
                    Field        = "deduction_code",
                    ErrorMessage = $"Deduction code '{row.DeductionCode}' not found or inactive."
                });
                continue; // cannot validate amounts without knowing the mode
            }

            ValidateModeAmounts(row, deduction.CalculationMode, errors);

            if (row.EffectiveEndDate.HasValue && row.EffectiveEndDate.Value < row.EffectiveStartDate)
                errors.Add(new RecordError
                {
                    RowNumber    = row.RowNumber,
                    Field        = "effective_end_date",
                    ErrorMessage = "effective_end_date must be on or after effective_start_date."
                });
        }

        var errorRows  = new HashSet<int>(errors.Select(e => e.RowNumber));
        var validCount = records.Count(r => !errorRows.Contains(r.RowNumber));

        return new BatchValidationResult
        {
            TotalRecords = totalDataRows,
            ValidCount   = validCount,
            InvalidCount = totalDataRows - validCount,
            Errors       = errors
        };
    }

    private static void ValidateModeAmounts(ImportRow row, string mode, List<RecordError> errors)
    {
        switch (mode)
        {
            case CalculationMode.FixedPerPeriod:
            case CalculationMode.FixedAnnual:
            case CalculationMode.FixedMonthly:
                if (row.EmployeeAmount is null or < 0)
                    errors.Add(new RecordError
                    {
                        RowNumber    = row.RowNumber,
                        Field        = "employee_amount",
                        ErrorMessage = $"employee_amount is required and must be ≥ 0 for {mode}."
                    });
                break;

            case CalculationMode.PctPreTax:
            case CalculationMode.PctPostTax:
                if (row.ContributionPct is null or < 0 or > 100)
                    errors.Add(new RecordError
                    {
                        RowNumber    = row.RowNumber,
                        Field        = "contribution_pct",
                        ErrorMessage = $"contribution_pct must be a number from 0 to 100 for {mode} (e.g. 6 means 6%)."
                    });
                break;

            case CalculationMode.CoverageBased:
                if (string.IsNullOrWhiteSpace(row.CoverageTier)
                    || !ValidCoverageTiers.Contains(row.CoverageTier))
                    errors.Add(new RecordError
                    {
                        RowNumber    = row.RowNumber,
                        Field        = "coverage_tier",
                        ErrorMessage = $"coverage_tier must be one of: {string.Join(", ", ValidCoverageTiers)}."
                    });
                break;
        }
    }

    // ── Submit ───────────────────────────────────────────────────────────

    public async Task<Guid> SubmitBatchAsync(
        Stream fileContent, string fileFormat, Guid submittedBy, CancellationToken ct = default)
    {
        var jobId = Guid.NewGuid();
        _logger.LogInformation("Benefits import job {JobId} started by {SubmittedBy}.", jobId, submittedBy);

        var (records, _, _) = await ParseCsvAsync(fileContent, ct);

        var deductionMap = (await _deductionRepository.GetActiveCodesAsync(ct))
            .ToDictionary(d => d.Code, StringComparer.OrdinalIgnoreCase);

        var employeeNumbers = records.Select(r => r.EmployeeNumber).Distinct().ToArray();
        var resolved        = await ResolveEmployeeNumbersAsync(employeeNumbers, ct);

        int accepted = 0, rejected = 0;

        foreach (var row in records)
        {
            ct.ThrowIfCancellationRequested();

            if (!resolved.TryGetValue(row.EmployeeNumber, out var employmentId)
                || !deductionMap.TryGetValue(row.DeductionCode, out var deduction))
            {
                rejected++;
                continue;
            }

            var mode = deduction.CalculationMode;

            decimal? employeeAmount  = null;
            decimal? contributionPct = null;
            string?  coverageTier    = null;

            switch (mode)
            {
                case CalculationMode.FixedPerPeriod:
                case CalculationMode.FixedAnnual:
                case CalculationMode.FixedMonthly:
                    if (row.EmployeeAmount is null or < 0) { rejected++; continue; }
                    employeeAmount = row.EmployeeAmount;
                    break;

                case CalculationMode.PctPreTax:
                case CalculationMode.PctPostTax:
                    if (row.ContributionPct is null or < 0 or > 100) { rejected++; continue; }
                    contributionPct = Math.Round(row.ContributionPct.Value / 100m, 6);
                    break;

                case CalculationMode.CoverageBased:
                    if (string.IsNullOrWhiteSpace(row.CoverageTier)
                        || !ValidCoverageTiers.Contains(row.CoverageTier))
                    { rejected++; continue; }
                    coverageTier = row.CoverageTier;
                    break;
            }

            try
            {
                await _electionService.ImportElectionAsync(new CreateElectionCommand
                {
                    EmploymentId               = employmentId,
                    DeductionId                = deduction.DeductionId,
                    EmployeeAmount             = employeeAmount,
                    EmployerContributionAmount = row.EmployerContributionAmount,
                    ContributionPct            = contributionPct,
                    CoverageTier               = coverageTier,
                    AnnualCoverageAmount       = row.AnnualCoverageAmount,
                    EffectiveStartDate         = row.EffectiveStartDate,
                    EffectiveEndDate           = row.EffectiveEndDate,
                    Source                     = "IMPORT",
                    CreatedBy                  = submittedBy
                }, ct);
                accepted++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Import job {JobId}: row {Row} failed.", jobId, row.RowNumber);
                rejected++;
            }
        }

        _logger.LogInformation(
            "Benefits import job {JobId} complete — accepted={Accepted} rejected={Rejected}.",
            jobId, accepted, rejected);

        return jobId;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    // ANSI-compliant parameterized IN — avoids Postgres-specific ANY(@Array).
    private async Task<Dictionary<string, Guid>> ResolveEmployeeNumbersAsync(
        string[] employeeNumbers, CancellationToken ct)
    {
        if (employeeNumbers.Length == 0) return [];
        using var conn = _connectionFactory.CreateConnection();
        var p = new DynamicParameters();
        var paramList = employeeNumbers.Select((n, i) => { p.Add($"N{i}", n); return $"@N{i}"; });
        var sql = $"""
            SELECT employee_number, employment_id
            FROM   employment
            WHERE  employee_number IN ({string.Join(",", paramList)})
            """;
        var rows = await conn.QueryAsync<(string EmployeeNumber, Guid EmploymentId)>(sql, p);
        return rows.ToDictionary(r => r.EmployeeNumber, r => r.EmploymentId, StringComparer.OrdinalIgnoreCase);
    }

    // CSV column layout (0-based, header row required):
    //  0  employee_number                required
    //  1  deduction_code                 required
    //  2  employee_amount                FIXED_* modes (≥ 0); blank for others
    //  3  contribution_pct               PCT_* modes (0–100, e.g. 6 = 6%); blank for others
    //  4  coverage_tier                  COVERAGE_BASED (EE_ONLY/EE_SPOUSE/EE_CHILD/FAMILY); blank for others
    //  5  employer_contribution_amount   optional
    //  6  annual_coverage_amount         optional
    //  7  effective_start_date           required (YYYY-MM-DD)
    //  8  effective_end_date             optional (YYYY-MM-DD)
    private static async Task<(List<ImportRow> records, int totalDataRows, List<RecordError> errors)>
        ParseCsvAsync(Stream fileContent, CancellationToken ct)
    {
        var records = new List<ImportRow>();
        var errors  = new List<RecordError>();
        int lineNum      = 0;
        int totalDataRows = 0;

        using var reader = new StreamReader(fileContent, leaveOpen: true);
        string? line;

        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            lineNum++;
            if (lineNum == 1) continue;                    // skip header
            if (string.IsNullOrWhiteSpace(line)) continue; // skip blank lines

            int rowNum = lineNum;
            totalDataRows++;

            var fields = line.Split(',');

            if (fields.Length < 8)
            {
                errors.Add(new RecordError
                {
                    RowNumber    = rowNum,
                    Field        = "row",
                    ErrorMessage = $"Row has {fields.Length} column(s) — expected at least 8."
                });
                continue;
            }

            var employeeNumber = fields[0].Trim();
            if (string.IsNullOrWhiteSpace(employeeNumber))
            {
                errors.Add(new RecordError
                {
                    RowNumber    = rowNum,
                    Field        = "employee_number",
                    ErrorMessage = "employee_number is required."
                });
                continue;
            }

            var deductionCode = fields[1].Trim();
            if (string.IsNullOrWhiteSpace(deductionCode))
            {
                errors.Add(new RecordError
                {
                    RowNumber    = rowNum,
                    Field        = "deduction_code",
                    ErrorMessage = "deduction_code is required."
                });
                continue;
            }

            if (!DateOnly.TryParse(fields[7].Trim(), out var startDate))
            {
                errors.Add(new RecordError
                {
                    RowNumber    = rowNum,
                    Field        = "effective_start_date",
                    ErrorMessage = "effective_start_date is required in YYYY-MM-DD format."
                });
                continue;
            }

            // Amount columns — lenient at parse time; mode validation happens in ValidateBatchAsync.
            decimal? employeeAmount    = TryParseDecimal(fields[2], out var ea) ? ea : null;
            decimal? contributionPct   = TryParseDecimal(fields[3], out var cp) ? cp : null;
            string?  coverageTier      = string.IsNullOrWhiteSpace(fields[4].Trim())
                                            ? null : fields[4].Trim().ToUpperInvariant();
            decimal? employerAmount    = fields.Length > 5 && TryParseDecimal(fields[5], out var emp) ? emp : null;
            decimal? annualCoverage    = fields.Length > 6 && TryParseDecimal(fields[6], out var aca) ? aca : null;

            // Emit format warnings for non-empty but unparseable numeric fields.
            if (!string.IsNullOrWhiteSpace(fields[2].Trim()) && employeeAmount is null)
                errors.Add(new RecordError { RowNumber = rowNum, Field = "employee_amount",
                    ErrorMessage = "employee_amount must be a decimal number." });

            if (!string.IsNullOrWhiteSpace(fields[3].Trim()) && contributionPct is null)
                errors.Add(new RecordError { RowNumber = rowNum, Field = "contribution_pct",
                    ErrorMessage = "contribution_pct must be a decimal number (e.g. 6 for 6%)." });

            DateOnly? endDate = null;
            if (fields.Length > 8 && !string.IsNullOrWhiteSpace(fields[8].Trim()))
            {
                if (DateOnly.TryParse(fields[8].Trim(), out var ed))
                    endDate = ed;
                else
                    errors.Add(new RecordError { RowNumber = rowNum, Field = "effective_end_date",
                        ErrorMessage = "effective_end_date must be in YYYY-MM-DD format." });
            }

            records.Add(new ImportRow
            {
                RowNumber                  = rowNum,
                EmployeeNumber             = employeeNumber,
                DeductionCode              = deductionCode,
                EmployeeAmount             = employeeAmount,
                ContributionPct            = contributionPct,
                CoverageTier               = coverageTier,
                EmployerContributionAmount = employerAmount,
                AnnualCoverageAmount       = annualCoverage,
                EffectiveStartDate         = startDate,
                EffectiveEndDate           = endDate
            });
        }

        return (records, totalDataRows, errors);
    }

    private static bool TryParseDecimal(string field, out decimal value) =>
        decimal.TryParse(field.Trim(), out value) && !string.IsNullOrWhiteSpace(field.Trim());

    private sealed record ImportRow
    {
        public int       RowNumber                  { get; init; }
        public string    EmployeeNumber             { get; init; } = string.Empty;
        public string    DeductionCode              { get; init; } = string.Empty;
        public decimal?  EmployeeAmount             { get; init; }
        public decimal?  ContributionPct            { get; init; }   // user value (e.g. 6); divided by 100 on save
        public string?   CoverageTier               { get; init; }
        public decimal?  EmployerContributionAmount { get; init; }
        public decimal?  AnnualCoverageAmount       { get; init; }
        public DateOnly  EffectiveStartDate         { get; init; }
        public DateOnly? EffectiveEndDate           { get; init; }
    }
}
