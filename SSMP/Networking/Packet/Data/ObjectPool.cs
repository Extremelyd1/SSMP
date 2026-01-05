using System.Collections.Concurrent;

namespace SSMP.Networking.Packet.Data;

/// <summary>
/// Interface for objects that can be pooled and reset for reuse.
/// Implementing classes should clear all state in Reset() to ensure
/// clean reuse without allocation.
/// </summary>
internal interface IPoolable {
    /// <summary>
    /// Resets the object to its initial state for reuse.
    /// Must clear all collections and reset all fields to defaults.
    /// </summary>
    void Reset();
}

/// <summary>
/// Thread-safe generic object pool for reducing allocations of frequently created objects.
/// Uses ConcurrentBag for lock-free operations in most cases.
/// </summary>
/// <typeparam name="T">Type of object to pool. Must be IPoolable and have parameterless constructor.</typeparam>
internal static class ObjectPool<T> where T : class, IPoolable, new() {
    /// <summary>
    /// Maximum number of objects to keep in the pool.
    /// Prevents unbounded memory growth.
    /// </summary>
    private const int MaxPoolSize = 64;

    /// <summary>
    /// Thread-safe bag of pooled objects.
    /// </summary>
    private static readonly ConcurrentBag<T> Pool = [];

    /// <summary>
    /// Current approximate count of pooled objects.
    /// </summary>
    // ReSharper disable once StaticMemberInGenericType
    private static int _count;

    /// <summary>
    /// Gets an object from the pool or creates a new one if pool is empty.
    /// </summary>
    /// <returns>A reset object ready for use.</returns>
    public static T Get() {
        if (!Pool.TryTake(out var item)) {
            return new T();
        }

        System.Threading.Interlocked.Decrement(ref _count);
        return item;
    }

    /// <summary>
    /// Returns an object to the pool after resetting it.
    /// If pool is full, the object is simply discarded for GC.
    /// </summary>
    /// <param name="item">The object to return to the pool.</param>
    public static void Return(T item) {
        // Atomically check and increment to prevent race conditions.
        // Multiple threads could otherwise pass the size check simultaneously.
        int currentCount;
        int newCount;
        do {
            currentCount = _count;
            if (currentCount >= MaxPoolSize) return;
            newCount = currentCount + 1;
        } while (System.Threading.Interlocked.CompareExchange(ref _count, newCount, currentCount) != currentCount);

        item.Reset();
        Pool.Add(item);
    }
}
