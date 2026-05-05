using System.Globalization;
using System.Text;
using Dapper;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Lookups;
using AllWorkHRIS.Module.TimeAttendance.Commands;
using AllWorkHRIS.Module.TimeAttendance.Domain;

namespace AllWorkHRIS.Module.TimeAttendance.Services;

public sealed class TimeImportService : ITimeImportService
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly ILookupCache       _lookupCache;
    private readonly ITimeEntryService  _timeEntryService;

    public TimeImportService(
        IConnectionFactory connectionFactory,
        ILookupCache       lookupCache,
        ITimeEntryService  timeEntryService)
    {
        _connectionFactory = connectionFactory;
        _lookupCache       = lookupCache;
        _timeEntryService  = timeEntryService;
    }

    public async Task<TimeImportResult> ImportAsync(
        Stream csv, Guid importedBy, Guid? scopedEntityId = null, CancellationToken ct = default)
    {
        // Resolve the active entity name once upfront for use in error messages
        var entityName = scopedEntityId.HasValue
            ? await ResolveEntityNameAsync(scopedEntityId.Value)
            : null;

        var validCategories = _lookupCache
            .GetAll(TimeAttendanceLookupTables.TimeCategory)
            .ToDictionary(e => e.Code, e => e.Code, StringComparer.OrdinalIgnoreCase);

        var rows          = ParseCsv(csv);
        var employeeCache = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);
        var periodCache   = new Dictionary<Guid, PeriodInfo?>();
        var errors        = new List<TimeImportError>();
        int imported      = 0;

        foreach (var (rowNum, row) in rows)
        {
            if (row.ParseError is not null)
            {
                errors.Add(new(rowNum, row.EmployeeNumber, row.ParseError));
                continue;
            }

            if (row.Duration <= 0)
            {
                errors.Add(new(rowNum, row.EmployeeNumber,
                    $"duration must be positive (was {row.Duration})."));
                continue;
            }

            if (row.StartTime.HasValue && row.EndTime.HasValue
                && row.EndTime.Value <= row.StartTime.Value)
            {
                errors.Add(new(rowNum, row.EmployeeNumber, "end_time must be after start_time."));
                continue;
            }

            if (!validCategories.TryGetValue(row.TimeCategory, out var canonicalCategory))
            {
                errors.Add(new(rowNum, row.EmployeeNumber,
                    $"Unknown time_category '{row.TimeCategory}'."));
                continue;
            }

            // Employee lookup scoped to the active entity (LE1)
            if (!employeeCache.TryGetValue(row.EmployeeNumber, out var employmentId))
            {
                employmentId = await ResolveEmployeeAsync(row.EmployeeNumber, scopedEntityId);
                employeeCache[row.EmployeeNumber] = employmentId;
            }
            if (employmentId is null)
            {
                var listName = entityName ?? "the active entity";
                errors.Add(new(rowNum, row.EmployeeNumber,
                    $"Employee '{row.EmployeeNumber}' not found in {listName}'s employee list."));
                continue;
            }

            // Period lookup — for date bounds only
            if (!periodCache.TryGetValue(row.PayrollPeriodId, out var periodInfo))
            {
                periodInfo = await ResolvePeriodAsync(row.PayrollPeriodId);
                periodCache[row.PayrollPeriodId] = periodInfo;
            }
            if (periodInfo is null)
            {
                errors.Add(new(rowNum, row.EmployeeNumber,
                    $"Payroll period '{row.PayrollPeriodId}' not found."));
                continue;
            }

            if (row.WorkDate < periodInfo.StartDate || row.WorkDate > periodInfo.EndDate)
            {
                errors.Add(new(rowNum, row.EmployeeNumber,
                    $"work_date {row.WorkDate:yyyy-MM-dd} is outside period range " +
                    $"{periodInfo.StartDate:yyyy-MM-dd}–{periodInfo.EndDate:yyyy-MM-dd}."));
                continue;
            }

            try
            {
                var command = new SubmitTimeEntryCommand
                {
                    EmploymentId    = employmentId.Value,
                    WorkDate        = row.WorkDate,
                    TimeCategory    = canonicalCategory,
                    Duration        = row.Duration,
                    StartTime       = row.StartTime,
                    EndTime         = row.EndTime,
                    PayrollPeriodId = row.PayrollPeriodId,
                    EntryMethod     = "IMPORT",
                    SubmittedBy     = importedBy,
                    Notes           = NullIfEmpty(row.Notes),
                    ProjectCode     = NullIfEmpty(row.ProjectCode),
                    TaskCode        = NullIfEmpty(row.TaskCode)
                };
                await _timeEntryService.SubmitTimeEntryAsync(command);
                imported++;
            }
            catch (Exception ex)
            {
                errors.Add(new(rowNum, row.EmployeeNumber, ex.Message));
            }
        }

        return new TimeImportResult(imported, errors.Count, errors);
    }

    private async Task<string?> ResolveEntityNameAsync(Guid entityId)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.ExecuteScalarAsync<string?>(
            "SELECT org_unit_name FROM org_unit WHERE org_unit_id = @EntityId",
            new { EntityId = entityId });
    }

    private async Task<Guid?> ResolveEmployeeAsync(string employeeNumber, Guid? legalEntityId)
    {
        using var conn = _connectionFactory.CreateConnection();
        if (legalEntityId.HasValue)
            return await conn.QueryFirstOrDefaultAsync<Guid?>(
                "SELECT employment_id FROM employment WHERE employee_number = @Num AND legal_entity_id = @EntityId",
                new { Num = employeeNumber, EntityId = legalEntityId.Value });

        return await conn.QueryFirstOrDefaultAsync<Guid?>(
            "SELECT employment_id FROM employment WHERE employee_number = @Num",
            new { Num = employeeNumber });
    }

    private async Task<PeriodInfo?> ResolvePeriodAsync(Guid periodId)
    {
        using var conn = _connectionFactory.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<PeriodRow>(
            """
            SELECT pp.period_start_date, pp.period_end_date, pp.calendar_status
            FROM   payroll_period pp
            WHERE  pp.period_id = @PeriodId
            """,
            new { PeriodId = periodId });
        if (row is null) return null;
        return new PeriodInfo(row.PeriodStartDate, row.PeriodEndDate, row.CalendarStatus);
    }

    private static List<(int RowNumber, ParsedRow Row)> ParseCsv(Stream stream)
    {
        var results = new List<(int, ParsedRow)>();
        using var reader = new StreamReader(stream, leaveOpen: true);

        var headerLine = reader.ReadLine();
        if (headerLine is null) return results;

        var headers  = SplitCsvLine(headerLine);
        var colIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Length; i++)
            colIndex[headers[i].Trim()] = i;

        int     rowNum = 1;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            rowNum++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cols = SplitCsvLine(line);
            string Get(string name) =>
                colIndex.TryGetValue(name, out var idx) && idx < cols.Length
                    ? cols[idx].Trim() : string.Empty;

            var employeeNumber = Get("employee_number");
            var timeCategory   = Get("time_category");
            var projectCode    = Get("project_code");
            var taskCode       = Get("task_code");
            var notes          = Get("notes");

            string?  parseError = null;
            DateOnly workDate   = DateOnly.MinValue;
            decimal  duration   = 0m;
            Guid     periodId   = Guid.Empty;
            TimeOnly? startTime = null;
            TimeOnly? endTime   = null;

            if (!DateOnly.TryParseExact(Get("work_date"), "yyyy-MM-dd", out workDate))
                parseError = "Invalid work_date format; expected yyyy-MM-dd.";
            else if (!decimal.TryParse(Get("duration"), NumberStyles.Number, CultureInfo.InvariantCulture, out duration))
                parseError = "Invalid duration; expected a decimal number.";
            else if (!Guid.TryParse(Get("payroll_period_id"), out periodId))
                parseError = "Invalid payroll_period_id; expected a UUID.";
            else
            {
                var startStr = Get("start_time");
                if (!string.IsNullOrEmpty(startStr))
                {
                    if (TimeOnly.TryParseExact(startStr, "HH:mm", out var st))
                        startTime = st;
                    else
                        parseError = $"Invalid start_time '{startStr}'; expected HH:mm.";
                }

                if (parseError is null)
                {
                    var endStr = Get("end_time");
                    if (!string.IsNullOrEmpty(endStr))
                    {
                        if (TimeOnly.TryParseExact(endStr, "HH:mm", out var et))
                            endTime = et;
                        else
                            parseError = $"Invalid end_time '{endStr}'; expected HH:mm.";
                    }
                }
            }

            results.Add((rowNum, new ParsedRow(
                EmployeeNumber:  employeeNumber,
                WorkDate:        workDate,
                TimeCategory:    timeCategory,
                Duration:        duration,
                StartTime:       startTime,
                EndTime:         endTime,
                PayrollPeriodId: periodId,
                ProjectCode:     projectCode,
                TaskCode:        taskCode,
                Notes:           notes,
                ParseError:      parseError)));
        }

        return results;
    }

    private static string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        int pos    = 0;

        while (pos <= line.Length)
        {
            if (pos == line.Length) { result.Add(string.Empty); break; }

            if (line[pos] == '"')
            {
                pos++;
                var sb = new StringBuilder();
                while (pos < line.Length)
                {
                    if (line[pos] == '"')
                    {
                        if (pos + 1 < line.Length && line[pos + 1] == '"')
                        { sb.Append('"'); pos += 2; }
                        else
                        { pos++; break; }
                    }
                    else
                    { sb.Append(line[pos++]); }
                }
                result.Add(sb.ToString());
                if (pos < line.Length && line[pos] == ',') pos++;
            }
            else
            {
                int start = pos;
                while (pos < line.Length && line[pos] != ',') pos++;
                result.Add(line[start..pos]);
                if (pos < line.Length) pos++;
            }
        }

        return [.. result];
    }

    private static string? NullIfEmpty(string s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;

    private sealed record ParsedRow(
        string   EmployeeNumber,
        DateOnly WorkDate,
        string   TimeCategory,
        decimal  Duration,
        TimeOnly? StartTime,
        TimeOnly? EndTime,
        Guid     PayrollPeriodId,
        string   ProjectCode,
        string   TaskCode,
        string   Notes,
        string?  ParseError);

    private sealed record PeriodRow
    {
        public DateOnly PeriodStartDate { get; init; }
        public DateOnly PeriodEndDate   { get; init; }
        public string   CalendarStatus  { get; init; } = string.Empty;
    }

    private sealed record PeriodInfo(DateOnly StartDate, DateOnly EndDate, string Status);
}
