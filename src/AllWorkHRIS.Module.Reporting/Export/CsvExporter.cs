using AllWorkHRIS.Module.Reporting.Domain;

namespace AllWorkHRIS.Module.Reporting.Export;

/// <summary>
/// Streams <see cref="ReportData"/> as RFC-4180 CSV. No external library — the
/// payload is small enough that the formatting rules are inlined.
/// </summary>
public sealed class CsvExporter
{
    public async Task<Stream> ExportAsync(ReportData data, CancellationToken ct = default)
    {
        var stream = new MemoryStream();
        await using (var writer = new StreamWriter(stream, leaveOpen: true))
        {
            // Header row
            await writer.WriteLineAsync(string.Join(",",
                data.Columns.Select(c => Quote(c.Header))));

            // Data rows
            foreach (var row in data.Rows)
            {
                ct.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(string.Join(",",
                    data.Columns.Select(c => Quote(FormatValue(row.TryGetValue(c.Field, out var v) ? v : null)))));
            }

            await writer.FlushAsync(ct);
        }

        stream.Position = 0;
        return stream;
    }

    private static string FormatValue(object? value) => value switch
    {
        null         => string.Empty,
        DateTime dt  => dt.ToString("yyyy-MM-dd"),
        DateOnly d   => d.ToString("yyyy-MM-dd"),
        decimal dec  => dec.ToString("0.##"),
        double dbl   => dbl.ToString("0.##"),
        _            => value.ToString() ?? string.Empty
    };

    private static string Quote(string v) =>
        v.Contains(',') || v.Contains('"') || v.Contains('\n')
            ? "\"" + v.Replace("\"", "\"\"") + "\""
            : v;
}
