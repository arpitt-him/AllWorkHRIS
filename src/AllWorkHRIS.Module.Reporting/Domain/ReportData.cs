namespace AllWorkHRIS.Module.Reporting.Domain;

/// <summary>
/// Materialised report result — column definitions plus row dictionaries.
/// Each row is keyed by <see cref="ReportColumn.Field"/>.
/// </summary>
public sealed record ReportData(
    IReadOnlyList<ReportColumn>                              Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object>>      Rows)
{
    public int RowCount => Rows.Count;

    /// <summary>
    /// Materialises a Dapper dynamic result set into a <see cref="ReportData"/>.
    /// Each <c>dynamic</c> row from Dapper is an <see cref="IDictionary{TKey,TValue}"/>.
    /// </summary>
    public static ReportData From(IEnumerable<dynamic> dynamicRows, IReadOnlyList<ReportColumn> columns)
    {
        var rows = dynamicRows
            .Cast<IDictionary<string, object>>()
            .Select(d =>
            {
                var dict = new Dictionary<string, object>(d.Count);
                foreach (var kv in d) dict[kv.Key] = kv.Value!;
                return (IReadOnlyDictionary<string, object>)dict;
            })
            .ToList();
        return new ReportData(columns, rows);
    }
}
