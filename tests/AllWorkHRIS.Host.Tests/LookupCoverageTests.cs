using System.Reflection;
using Dapper;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Core.Lookups;

namespace AllWorkHRIS.Host.Tests;

/// <summary>
/// Verifies that every table name declared in LookupTables is present in the
/// database and loaded by LookupCache with at least one entry.
/// Catches drift between the constants file and the actual schema / seed data.
/// </summary>
public class LookupCoverageTests
{
    private readonly ILookupCache _cache;

    public LookupCoverageTests()
    {
        Environment.SetEnvironmentVariable("DATABASE_CONNECTION_STRING",
            "Host=localhost;Database=allworkhris_dev;Username=postgres;Password=dev");
        Environment.SetEnvironmentVariable("DATABASE_PROVIDER", "postgresql");

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var connectionFactory = new ConnectionFactory();
        var cache = new LookupCache(connectionFactory);
        cache.RefreshAsync().GetAwaiter().GetResult();
        _cache = cache;
    }

    public static IEnumerable<object[]> AllTableNames()
    {
        // Reflect over every public string constant in LookupTables
        var fields = typeof(LookupTables)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string));

        foreach (var f in fields)
            yield return [f.Name, (string)f.GetRawConstantValue()!];
    }

    [Theory]
    [MemberData(nameof(AllTableNames))]
    public void LookupTable_HasAtLeastOneEntry(string constantName, string tableName)
    {
        var entries = _cache.GetAll(tableName);
        Assert.True(entries.Count > 0,
            $"LookupTables.{constantName} ('{tableName}') returned no entries from cache. " +
            "Either the table is missing from the DB, has no rows, or LookupCache does not load it.");
    }
}
