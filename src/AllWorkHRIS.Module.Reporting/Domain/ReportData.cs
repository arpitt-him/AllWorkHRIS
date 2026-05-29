using System.Dynamic;

namespace AllWorkHRIS.Module.Reporting.Domain;

/// <summary>
/// Materialised report result — column definitions plus row records.
///
/// Rows are <see cref="ExpandoObject"/> rather than a plain dictionary so
/// SfGrid's Field-based binding and native aggregate machinery treat each
/// column-key as a dynamic property. ExpandoObject also implements
/// <see cref="IDictionary{TKey,TValue}"/>, so the exporters can still read
/// values by key via a single cast.
/// </summary>
public sealed record ReportData(
    IReadOnlyList<ReportColumn>  Columns,
    IReadOnlyList<ExpandoObject> Rows)
{
    public int RowCount => Rows.Count;

    /// <summary>
    /// Materialises a Dapper dynamic result set into a <see cref="ReportData"/>.
    /// Each <c>dynamic</c> row from Dapper is an <see cref="IDictionary{TKey,TValue}"/>;
    /// we copy it into an <see cref="ExpandoObject"/> for downstream consumers.
    /// </summary>
    public static ReportData From(IEnumerable<dynamic> dynamicRows, IReadOnlyList<ReportColumn> columns)
    {
        var rows = dynamicRows
            .Cast<IDictionary<string, object>>()
            .Select(d =>
            {
                var expando = new ExpandoObject();
                var bag     = (IDictionary<string, object?>)expando;
                foreach (var kv in d) bag[kv.Key] = kv.Value;
                return expando;
            })
            .ToList();
        return new ReportData(columns, rows);
    }
}
