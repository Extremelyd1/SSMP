using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace SSMP.Util;

/// <summary>
/// Class for utilities regarding threading.
/// </summary>
internal class ThreadUtil : MonoBehaviour {
    /// <summary>
    /// Queue of actions that need to be run on the Unity main thread.
    /// </summary>
    private static readonly ConcurrentQueue<Action> ActionsToRun = new ConcurrentQueue<Action>();

    /// <summary>
    /// Instantiate this static class.
    /// </summary>
    public static void Instantiate() {
        var threadUtilObject = new GameObject();
        threadUtilObject.AddComponent<ThreadUtil>();
        DontDestroyOnLoad(threadUtilObject);
    }

    /// <summary>
    /// Runs the given action on the main thread of Unity.
    /// </summary>
    /// <param name="action">The action to run.</param>
    public static void RunActionOnMainThread(Action action) {
        ActionsToRun.Enqueue(action);
    }

    public void Update() {
        // Only drain what is queued now; actions enqueued by these actions run next frame
        var count = ActionsToRun.Count;
        while (count-- > 0 && ActionsToRun.TryDequeue(out var action)) {
            action.Invoke();
        }
    }
}
