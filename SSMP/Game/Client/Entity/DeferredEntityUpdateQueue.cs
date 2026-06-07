using System;
using System.Collections.Generic;
using SSMP.Networking.Packet.Data;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Game.Client.Entity;

/// <summary>
/// Buffers entity updates until their target entity and scene role are ready.
/// </summary>
internal class DeferredEntityUpdateQueue {
    /// <summary>
    /// The buffered updates awaiting a retry.
    /// </summary>
    private readonly Queue<BaseEntityUpdate> _updates = [];

    /// <summary>
    /// Store an update for a later retry.
    /// </summary>
    /// <param name="update">The update to store.</param>
    public void Enqueue(BaseEntityUpdate update) {
        _updates.Enqueue(update);
    }

    /// <summary>
    /// Clear all deferred updates.
    /// </summary>
    public void Clear() {
        _updates.Clear();
    }

    /// <summary>
    /// Retry deferred updates once. Updates that still cannot be applied are re-queued.
    /// </summary>
    /// <param name="apply">Callback that returns true when the update was applied.</param>
    public void Retry(Func<BaseEntityUpdate, bool> apply) {
        var count = _updates.Count;

        for (var i = 0; i < count; i++) {
            var update = _updates.Dequeue();

            if (apply(update)) {
                continue;
            }

            _updates.Enqueue(update);
        }
    }

    /// <summary>
    /// Logs and discards an update type that cannot be retried.
    /// </summary>
    /// <param name="update">The unknown update.</param>
    public static void DiscardUnknown(BaseEntityUpdate update) {
        Logger.Warn($"Unknown update type in deferred queue, discarding: {update.GetType()}");
    }
}
