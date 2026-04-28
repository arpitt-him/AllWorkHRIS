using AllWorkHRIS.Core.Data;

namespace AllWorkHRIS.Host;

public sealed class TenantConnectionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TenantRegistry _tenantRegistry;

    public TenantConnectionMiddleware(RequestDelegate next, TenantRegistry tenantRegistry)
    {
        _next = next;
        _tenantRegistry = tenantRegistry;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only apply tenant resolution to authenticated requests
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var tenantId = context.User.FindFirst("tenant_id")?.Value;

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("tenant_id claim is missing from token.");
                return;
            }

            var factory = _tenantRegistry.ResolveFactory(tenantId);

            // Make the resolved factory available for the duration of this request
            context.Items["IConnectionFactory"] = factory;
        }

        await _next(context);
    }
}
