using AllWorkHRIS.Module.Reporting.Domain;
using ClosedXML.Excel;

namespace AllWorkHRIS.Module.Reporting.Export;

/// <summary>
/// Materialises a <see cref="ReportData"/> as a styled XLSX workbook via
/// ClosedXML — title block, frozen header, currency / date formatting,
/// alternating row shading, optional totals row.
/// </summary>
public sealed class XlsxExporter
{
    public Stream Export(
        ReportData       data,
        ReportDefinition definition,
        string           parameterSummary,
        DateTime         generatedAtLocal)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(Truncate(definition.ShortName, 31));

        var colCount = data.Columns.Count;
        var hasParamSummary = !string.IsNullOrWhiteSpace(parameterSummary);

        // Title block — row 1
        var titleCell = sheet.Cell(1, 1);
        titleCell.Value = definition.Title;
        titleCell.Style.Font.Bold     = true;
        titleCell.Style.Font.FontSize = 13;
        if (colCount > 1) sheet.Range(1, 1, 1, colCount).Merge();

        // Parameter summary — row 2. Falls back to a Generated stamp when no
        // per-report summary is available, so the row is never empty and the
        // freeze line at row 3 stays stable.
        var paramCell = sheet.Cell(2, 1);
        paramCell.Value = hasParamSummary
            ? parameterSummary
            : $"Generated: {generatedAtLocal:yyyy-MM-dd HH:mm}";
        paramCell.Style.Font.Italic   = true;
        paramCell.Style.Font.FontSize = 9;
        if (colCount > 1) sheet.Range(2, 1, 2, colCount).Merge();

        // Column headers — row 3
        for (int c = 0; c < colCount; c++)
        {
            var cell = sheet.Cell(3, c + 1);
            cell.Value                      = data.Columns[c].Header;
            cell.Style.Font.Bold            = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EDF2");
            cell.Style.Border.BottomBorder  = XLBorderStyleValues.Medium;
        }
        sheet.SheetView.FreezeRows(3);

        // Data rows — starting row 4
        for (int r = 0; r < data.Rows.Count; r++)
        {
            var row = (IDictionary<string, object?>)data.Rows[r];
            for (int c = 0; c < colCount; c++)
            {
                var col   = data.Columns[c];
                var cell  = sheet.Cell(r + 4, c + 1);
                var value = row.TryGetValue(col.Field, out var v) ? v : null;

                cell.Value = ToCellValue(value);

                if (col.Format == "currency")
                    cell.Style.NumberFormat.Format = "#,##0.00";
                else if (col.Format == "number")
                    cell.Style.NumberFormat.Format = "#,##0";
                else if (col.Format == "date")
                    cell.Style.NumberFormat.Format = "yyyy-mm-dd";

                if (r % 2 == 1)
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F7F9FC");
            }
        }

        int lastBodyRow = 4 + data.Rows.Count - 1;
        if (definition.ShowTotals)
        {
            AddTotalsRow(sheet, data, startDataRow: 4);
            lastBodyRow += 1;
        }

        // Generated stamp — only when row 2 already carried a real parameter
        // summary, so the timestamp lives below the data instead of crowding
        // the title block.
        if (hasParamSummary)
        {
            var stampRow  = lastBodyRow + 2;
            var stampCell = sheet.Cell(stampRow, 1);
            stampCell.Value = $"Generated: {generatedAtLocal:yyyy-MM-dd HH:mm}";
            stampCell.Style.Font.Italic   = true;
            stampCell.Style.Font.FontSize = 9;
            stampCell.Style.Font.FontColor = XLColor.FromHtml("#6B7280");
        }

        sheet.Columns().AdjustToContents(1, colCount, 8, 40);

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static XLCellValue ToCellValue(object? value) => value switch
    {
        null         => Blank.Value,
        decimal dec  => dec,
        double dbl   => dbl,
        int i        => i,
        long l       => l,
        bool b       => b,
        DateTime dt  => dt,
        DateOnly d   => d.ToDateTime(TimeOnly.MinValue),
        _            => value.ToString() ?? string.Empty
    };

    private static void AddTotalsRow(IXLWorksheet sheet, ReportData data, int startDataRow)
    {
        int totalsRow = startDataRow + data.Rows.Count;
        int colCount  = data.Columns.Count;

        for (int c = 0; c < colCount; c++)
        {
            var col  = data.Columns[c];
            var cell = sheet.Cell(totalsRow, c + 1);

            if (col.Format is "currency" or "number")
            {
                var topCell    = sheet.Cell(startDataRow,                  c + 1).Address.ToString();
                var bottomCell = sheet.Cell(startDataRow + data.Rows.Count - 1, c + 1).Address.ToString();
                cell.FormulaA1 = $"SUM({topCell}:{bottomCell})";
                cell.Style.NumberFormat.Format = col.Format == "currency" ? "#,##0.00" : "#,##0";
            }
            else if (c == 0)
            {
                cell.Value = "Total";
            }

            cell.Style.Font.Bold         = true;
            cell.Style.Border.TopBorder  = XLBorderStyleValues.Medium;
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max];
}
