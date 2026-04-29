namespace AllWorkHRIS.Core.Lookups;

public interface ILookupCache
{
    /// <summary>Resolve a code string to its integer id. Throws if not found.</summary>
    int GetId(string tableName, string code);

    /// <summary>Resolve an integer id to its code string. Throws if not found.</summary>
    string GetCode(string tableName, int id);

    /// <summary>Get the full LookupEntry for a given code. Throws if not found.</summary>
    LookupEntry Get(string tableName, string code);

    /// <summary>Get all active entries for a table ordered by sort_order.</summary>
    IReadOnlyList<LookupEntry> GetAll(string tableName);

    /// <summary>Reload all lookup tables from the database.</summary>
    Task RefreshAsync();
}
