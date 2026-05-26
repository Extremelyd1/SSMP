using System;
using UnityEngine;

namespace SSMP.Util;

/// <summary>
/// MonoBehaviour that fires <see cref="OnTick"/> every Unity frame, independent of <see cref="Time.timeScale"/>.
/// </summary>
internal class NetworkTickBehaviour : MonoBehaviour {
    /// <summary>
    /// Callback invoked once per frame.
    /// </summary>
    public Action? OnTick;

    private void Update() {
        OnTick?.Invoke();
    }
}
