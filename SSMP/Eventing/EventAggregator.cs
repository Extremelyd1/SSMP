using System;
using System.Collections.Concurrent;
using SSMP.Api.Eventing;

namespace SSMP.Eventing;

/// <inheritdoc />
internal class EventAggregator : IEventAggregator {
    /// <summary>
    /// Dictionary mapping event types to their <see cref="InterEvent"/> instances.
    /// </summary>
    private readonly ConcurrentDictionary<Type, InterEvent> _events = new();

    /// <inheritdoc />
    public TEventType GetEvent<TEventType>() where TEventType : InterEvent, new() {
        // GetOrAdd may run the factory more than once under contention, but only one instance is ever stored
        // and returned to all callers
        return (TEventType) _events.GetOrAdd(typeof(TEventType), _ => new TEventType());
    }
}
