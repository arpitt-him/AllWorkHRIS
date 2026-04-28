// AllWorkHRIS.Core/EnvironmentValidator.cs
namespace AllWorkHRIS.Core;

public static class EnvironmentValidator
{
    /// <summary>
    /// Validates that all named environment variables are present and non-empty.
    /// Throws InvalidOperationException listing all missing variables if any are absent.
    /// Call this early in Program.cs before any services are registered.
    /// </summary>
    public static void ValidateRequired(params string[] variableNames)
    {
        var missing = variableNames
            .Where(name => string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(name)))
            .ToList();

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Required environment variables are not set: " +
                $"{string.Join(", ", missing)}. " +
                $"Check your environment configuration before starting the application.");
    }
}
