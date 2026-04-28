// AllWorkHRIS.Core/Events/IEventPublisher.cs
namespace AllWorkHRIS.Core.Events;

public interface IEventPublisher
{
    /// <summary>
    /// Publishes an event to all registered handlers for type T.
    /// If no handlers are registered, this is a silent no-op.
    /// </summary>
    Task PublishAsync<T>(T payload) where T : class;

    /// <summary>
    /// Registers a handler for event type T.
    /// Called by modules at startup via IPlatformModule.Register.
    /// If no handlers are registered for T, PublishAsync is a no-op.
    /// </summary>
    void RegisterHandler<T>(Func<T, Task> handler) where T : class;
}
