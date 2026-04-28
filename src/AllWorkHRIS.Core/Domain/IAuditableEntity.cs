// AllWorkHRIS.Core/Domain/IAuditableEntity.cs
namespace AllWorkHRIS.Core.Domain;

/// <summary>
/// Marker interface for entities that carry standard audit fields.
/// Implementing entities must expose created and last-updated timestamps
/// and the identity of the actor who created them.
/// </summary>
public interface IAuditableEntity
{
    Guid CreatedBy { get; }
    DateTimeOffset CreatedTimestamp { get; }
    DateTimeOffset LastUpdateTimestamp { get; }
}
