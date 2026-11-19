using System;
using System.Linq;
using SSMP.Eventing;
using SSMP.Logging;

namespace SSMP.Api.Eventing;

/// <summary>
/// Base type for all inter-mod events (e.g. from one client-side mod to another client-side mod on the same device).
/// </summary>
public class InterEvent {
    /// <summary>
    /// Copy-on-write array containing event subscriptions for this event. Writers replace the array under
    /// <see cref="_lock"/>; publishers iterate over the array they read, so handlers may (un)subscribe safely.
    /// </summary>
    private volatile EventSubscription[] _subscriptions = [];

    /// <summary>
    /// Object to lock writes to <see cref="_subscriptions"/>.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// Unsubscribes an action from the event by its token.
    /// </summary>
    /// <param name="token">The token that denotes the subscription.</param>
    private void Unsubscribe(SubscriptionToken token) {
        lock (_lock) {
            _subscriptions = _subscriptions.Where(s => !s.SubscriptionToken.Equals(token)).ToArray();
        }
    }

    /// <summary>
    /// Internal implementation of subscribe, with types erased.
    /// </summary>
    /// <param name="strategy">The strategy to execute when an event is triggered.</param>
    /// <returns>A token that represents this subscription and can be used to unsubscribe.</returns>
    protected SubscriptionToken SubscribeInternal(Action<object> strategy) {
        var sub = new EventSubscription(new SubscriptionToken(Unsubscribe), strategy);

        lock (_lock) {
            _subscriptions = [.._subscriptions, sub];
        }

        return sub.SubscriptionToken;
    }

    /// <summary>
    /// Internal implementation of publish, with types erased.
    /// </summary>
    /// <param name="payload">The payload object to publish.</param>
    protected void PublishInternal(object payload) {
        // Reading without the lock is safe, since the array is only ever replaced and never modified
        // ReSharper disable once InconsistentlySynchronizedField
        foreach (var subscription in _subscriptions) {
            try {
                subscription.ExecutionStrategy(payload);
            } catch (Exception e) {
                Logger.Error($"Exception in event handler for {GetType()}: {e}");
            }
        }
    }
}
