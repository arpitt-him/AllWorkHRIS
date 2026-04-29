using System.Reflection;
using Dapper;
using AllWorkHRIS.Core.Data;

namespace AllWorkHRIS.Core.Lookups;

public sealed class LookupCache : ILookupCache
{
    private readonly IConnectionFactory _connectionFactory;

    private Dictionary<string, Dictionary<string, LookupEntry>> _byCode = new();
    private Dictionary<string, Dictionary<int, LookupEntry>>    _byId   = new();

    public LookupCache(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task InitialiseAsync() => await LoadAsync();

    public async Task RefreshAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        var byCode = new Dictionary<string, Dictionary<string, LookupEntry>>(
            StringComparer.OrdinalIgnoreCase);
        var byId = new Dictionary<string, Dictionary<int, LookupEntry>>();

        using var conn = _connectionFactory.CreateConnection();

        foreach (var tableName in AllTables())
        {
            var sql = $"SELECT id, code, label FROM {tableName} WHERE is_active = true ORDER BY sort_order, id";

            IEnumerable<LookupEntry> entries;
            try
            {
                entries = await conn.QueryAsync<LookupEntry>(sql);
            }
            catch
            {
                // Table may not exist yet (payroll tables in HRIS-only deployment)
                continue;
            }

            var codeDict = new Dictionary<string, LookupEntry>(StringComparer.OrdinalIgnoreCase);
            var idDict   = new Dictionary<int, LookupEntry>();

            foreach (var entry in entries)
            {
                codeDict[entry.Code] = entry;
                idDict[entry.Id]     = entry;
            }

            byCode[tableName] = codeDict;
            byId[tableName]   = idDict;
        }

        _byCode = byCode;
        _byId   = byId;
    }

    public int GetId(string tableName, string code)
    {
        if (_byCode.TryGetValue(tableName, out var table) &&
            table.TryGetValue(code, out var entry))
            return entry.Id;

        throw new InvalidOperationException(
            $"Lookup code '{code}' not found in table '{tableName}'.");
    }

    public string GetCode(string tableName, int id)
    {
        if (_byId.TryGetValue(tableName, out var table) &&
            table.TryGetValue(id, out var entry))
            return entry.Code;

        throw new InvalidOperationException(
            $"Lookup id '{id}' not found in table '{tableName}'.");
    }

    public LookupEntry Get(string tableName, string code)
    {
        if (_byCode.TryGetValue(tableName, out var table) &&
            table.TryGetValue(code, out var entry))
            return entry;

        throw new InvalidOperationException(
            $"Lookup code '{code}' not found in table '{tableName}'.");
    }

    public IReadOnlyList<LookupEntry> GetAll(string tableName)
    {
        if (_byCode.TryGetValue(tableName, out var table))
            return table.Values.ToList();

        throw new InvalidOperationException(
            $"Lookup table '{tableName}' not found in cache.");
    }

    private static IEnumerable<string> AllTables() =>
        typeof(LookupTables)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!);
}
