using AllWorkHRIS.Module.Reporting.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AllWorkHRIS.Module.Reporting.Export;

/// <summary>
/// Materialises a <see cref="ReportData"/> as a styled PDF via QuestPDF — title
/// + parameter summary in the header, paginated table with alternating row
/// shading, page numbers in the footer.
/// </summary>
public sealed class PdfExporter
{
    public Stream Export(
        ReportData       data,
        ReportDefinition definition,
        string           parameterSummary,
        DateTime         generatedAtLocal)
    {
        var stream = new MemoryStream();
        var hasParamSummary = !string.IsNullOrWhiteSpace(parameterSummary);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(definition.IsWideReport
                    ? PageSizes.A4.Landscape()
                    : PageSizes.A4);

                page.Margin(30);
                page.DefaultTextStyle(t => t.FontSize(8));

                page.Header().Column(col =>
                {
                    col.Item().Text(definition.Title).Bold().FontSize(14);
                    if (hasParamSummary)
                    {
                        col.Item().Text(parameterSummary)
                           .Italic().FontSize(8).FontColor(Colors.Grey.Darken2);
                    }
                    col.Item().Height(4);
                    col.Item().LineHorizontal(0.5f);
                });

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        foreach (var _ in data.Columns) cols.RelativeColumn();
                    });

                    // Header row
                    table.Header(header =>
                    {
                        foreach (var col in data.Columns)
                        {
                            header.Cell()
                                  .Background("#E8EDF2")
                                  .Padding(4)
                                  .Text(col.Header)
                                  .Bold();
                        }
                    });

                    // Data rows
                    bool alternate = false;
                    foreach (var expandoRow in data.Rows)
                    {
                        var row = (IDictionary<string, object?>)expandoRow;
                        string bg = alternate ? "#F7F9FC" : "#FFFFFF";
                        foreach (var col in data.Columns)
                        {
                            var value     = row.TryGetValue(col.Field, out var v) ? v : null;
                            var formatted = FormatValue(value, col.Format);
                            var isNumeric = col.Format is "currency" or "number";

                            var cell = table.Cell().Background(bg).Padding(3);
                            if (isNumeric)
                                cell.AlignRight().Text(formatted).FontSize(7.5f);
                            else
                                cell.Text(formatted).FontSize(7.5f);
                        }
                        alternate = !alternate;
                    }

                    // Totals row — same per-column rule as XLSX: SUM for any
                    // currency/number column, "Total" label in the first
                    // non-numeric cell, blank elsewhere. Bold + top border so
                    // it reads as a summary stripe rather than another data row.
                    if (definition.ShowTotals)
                    {
                        var totals = ComputeColumnTotals(data);
                        bool labeled = false;
                        foreach (var col in data.Columns)
                        {
                            var cell = table.Cell()
                                            .Background("#E8EDF2")
                                            .BorderTop(0.75f)
                                            .BorderColor(Colors.Grey.Darken1)
                                            .Padding(3);
                            if (col.Format is "currency" or "number" && totals.TryGetValue(col.Field, out var t))
                            {
                                var formatted = col.Format == "currency" ? t.ToString("N2") : t.ToString("N0");
                                cell.AlignRight().Text(formatted).Bold().FontSize(7.5f);
                            }
                            else if (!labeled)
                            {
                                cell.Text("Total").Bold().FontSize(7.5f);
                                labeled = true;
                            }
                            else
                            {
                                cell.Text(string.Empty);
                            }
                        }
                    }
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem()
                       .Text($"Generated: {generatedAtLocal:yyyy-MM-dd HH:mm}")
                       .FontSize(7).FontColor(Colors.Grey.Medium);
                    row.RelativeItem().AlignRight().Text(txt =>
                    {
                        txt.Span("Page ").FontSize(7);
                        txt.CurrentPageNumber().FontSize(7);
                        txt.Span(" of ").FontSize(7);
                        txt.TotalPages().FontSize(7);
                    });
                });
            });
        }).GeneratePdf(stream);

        stream.Position = 0;
        return stream;
    }

    private static Dictionary<string, decimal> ComputeColumnTotals(ReportData data)
    {
        var totals = new Dictionary<string, decimal>();
        foreach (var col in data.Columns)
        {
            if (col.Format is not ("currency" or "number")) continue;
            decimal sum = 0m;
            foreach (var expandoRow in data.Rows)
            {
                var row = (IDictionary<string, object?>)expandoRow;
                if (!row.TryGetValue(col.Field, out var v) || v is null) continue;
                try { sum += Convert.ToDecimal(v, System.Globalization.CultureInfo.InvariantCulture); }
                catch { /* non-numeric — skip */ }
            }
            totals[col.Field] = sum;
        }
        return totals;
    }

    private static string FormatValue(object? value, string? format) => value switch
    {
        null                                 => string.Empty,
        decimal dec when format == "currency" => dec.ToString("N2"),
        decimal dec when format == "number"   => dec.ToString("N0"),
        decimal dec                           => dec.ToString("0.##"),
        double dbl  when format == "currency" => dbl.ToString("N2"),
        double dbl                            => dbl.ToString("0.##"),
        DateTime dt                          => dt.ToString("yyyy-MM-dd"),
        DateOnly d                           => d.ToString("yyyy-MM-dd"),
        _                                    => value.ToString() ?? string.Empty
    };

}
