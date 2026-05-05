// AllWorkHRIS.Core/Events/InProcessEventBus.cs
using System.Collections.Concurrent;

namespace AllWorkHRIS.Core.Events;

public sealed class InProcessEventBus : IEventPublisher
{
    private readonly ConcurrentDictionary<Type, List<Func<object, Task>>> _handlers = new();

    public void RegisterHandler<T>(Func<T, Task> handler) where T : class
    {
        _handlers.GetOrAdd(typeof(T), _ => [])
                 .Add(payload => handler((T)payload));
    }

    public async Task PublishAsync<T>(T payload) where T : class
    {
        if (_handlers.TryGetValue(typeof(T), out var handlers))
            foreach (var handler in handlers)
                await handler(payload);

        // No handlers registered = silent no-op.
        // This is the correct and expected behavior in HRIS-only deployments.
    }
}
