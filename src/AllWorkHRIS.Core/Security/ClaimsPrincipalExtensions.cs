// AllWorkHRIS.Core/Security/ClaimsPrincipalExtensions.cs
using System.Security.Claims;

namespace AllWorkHRIS.Core.Security;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Returns the tenant_id claim value as a Guid.
    /// Throws if the claim is missing or not a valid Guid.
    /// </summary>
    public static Guid GetTenantId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirst("tenant_id")?.Value
            ?? throw new InvalidOperationException(
                "tenant_id claim is missing from the current principal.");

        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException(
                $"tenant_id claim value '{value}' is not a valid Guid.");
    }

    /// <summary>
    /// Returns the employment_id claim value as a Guid.
    /// Returns null if the claim is absent (e.g. admin users with no employment record).
    /// </summary>
    public static Guid? GetEmploymentId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirst("employment_id")?.Value;
        if (value is null) return null;

        return Guid.TryParse(value, out var id) ? id : null;
    }

    /// <summary>
    /// Returns true if the principal holds any of the specified roles.
    /// Uses direct claim inspection rather than IsInRole() to work with
    /// Keycloak realm roles and MapInboundClaims = false.
    /// </summary>
    public static bool HasAnyRole(this ClaimsPrincipal principal, params string[] roles)
        => roles.Any(r => principal.Claims.Any(c => c.Type == "roles" && c.Value == r));

    /// <summary>
    /// Returns all role claim values for the principal.
    /// </summary>
    public static IEnumerable<string> GetRoles(this ClaimsPrincipal principal)
        => principal.Claims.Where(c => c.Type == "roles").Select(c => c.Value);

    /// <summary>
    /// Returns a display name for the current principal.
    /// Prefers 'name' claim, falls back to 'preferred_username', then 'sub'.
    /// </summary>
    public static string GetDisplayName(this ClaimsPrincipal principal)
    {
        return principal.FindFirst("name")?.Value
            ?? principal.FindFirst("preferred_username")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "Unknown";
    }
}
