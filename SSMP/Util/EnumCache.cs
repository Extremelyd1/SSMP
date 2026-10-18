using System;

namespace SSMP.Util;

/// <summary>
/// A helper class to cache properties of enum types to avoid repeating allocations and reflection overhead.
/// </summary>
/// <typeparam name="T">The enum type.</typeparam>
public static class EnumCache<T> where T : struct, Enum {
    /// <summary>
    /// The number of values defined in the enum.
    /// </summary>
    public static readonly int Count = Enum.GetNames(typeof(T)).Length;
}
