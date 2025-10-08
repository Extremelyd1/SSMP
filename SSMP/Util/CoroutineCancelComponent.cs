using System.Collections.Generic;
using UnityEngine;

namespace SSMP.Util;

/// <summary>
/// Component that track active coroutine on a GameObject so they can be cancelled on demand.
/// </summary>
internal class CoroutineCancelComponent : MonoBehaviour {
    /// <summary>
    /// Dictionary mapping string IDs to coroutines.
    /// </summary>
    private readonly Dictionary<string, Coroutine> _activeCoroutines = new();

    /// <summary>
    /// Add a coroutine with the given ID.
    /// </summary>
    /// <param name="id">The ID of the coroutine.</param>
    /// <param name="coroutine">The coroutine instance.</param>
    public void AddCoroutine(string id, Coroutine coroutine) {
        if (_activeCoroutines.ContainsKey(id)) {
            CancelCoroutine(id);
        }

        _activeCoroutines.Add(id, coroutine);
    }

    /// <summary>
    /// Cancel the coroutine with the given ID.
    /// </summary>
    /// <param name="id">The ID of the coroutine to cancel.</param>
    public void CancelCoroutine(string id) {
        if (!_activeCoroutines.TryGetValue(id, out var coroutine)) {
            return;
        }

        StopCoroutine(coroutine);
        _activeCoroutines.Remove(id);
    }
}
