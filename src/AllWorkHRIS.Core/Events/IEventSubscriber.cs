namespace AllWorkHRIS.Core.Events;

/// <summary>
/// Implemented by any module that needs to subscribe handlers to the event bus.
/// Program.cs resolves all registrations and calls RegisterHandlers once after build.
/// </summary>
public interface IEventSubscriber
{
    void RegisterHandlers(IEventPublisher eventPublisher);
}
