using Dapper;

namespace AllWorkHRIS.Module.TimeAttendance.Repositories;

/// <summary>
/// Builds a DBMS-agnostic SQL <c>IN (…)</c> list from integer ids. This stack does not wire Dapper's
/// list-expansion (a bare <c>IN @ids</c> emits a Postgres array placeholder and fails 42601), and we
/// avoid the Postgres-specific <c>= ANY(@ids)</c>. Instead we add each id as its own parameter and
/// emit <c>(@p0,@p1,…)</c> — portable across providers. Mirrors the pattern in PayrollRepositories.
/// </summary>
internal static class SqlInList
{
    /// <summary>
    /// Adds each value to <paramref name="parameters"/> under <c>{prefix}{n}</c> and returns the
    /// parenthesised placeholder list <c>(@{prefix}0,@{prefix}1,…)</c> for embedding after <c>IN</c>.
    /// Callers must guard against an empty collection (an empty IN-list is invalid SQL).
    /// </summary>
    public static string BuildInts(DynamicParameters parameters, IReadOnlyCollection<int> values, string prefix)
    {
        var names = new List<string>(values.Count);
        var i = 0;
        foreach (var v in values)
        {
            var name = $"{prefix}{i++}";
            parameters.Add(name, v);
            names.Add($"@{name}");
        }
        return "(" + string.Join(",", names) + ")";
    }
}
