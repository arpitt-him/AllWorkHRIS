using AllWorkHRIS.Core.Data;

namespace AllWorkHRIS.Host;

public sealed class TenantConfig
{
    public required string TenantId { get; init; }
    public required IConnectionFactory ConnectionFactory { get; init; }
}

public sealed class TenantRegistry
{
    private readonly Dictionary<string, IConnectionFactory> _factories;

    public TenantRegistry(IEnumerable<TenantConfig> configs)
    {
        _factories = configs.ToDictionary(
            c => c.TenantId,
            c => c.ConnectionFactory);
    }

    public IConnectionFactory ResolveFactory(string tenantId)
    {
        if (_factories.TryGetValue(tenantId, out var factory))
            return factory;

        throw new UnauthorizedAccessException(
            $"Unknown or unauthorised tenant: {tenantId}");
    }
}
