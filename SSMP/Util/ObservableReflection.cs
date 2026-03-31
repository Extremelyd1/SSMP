using System;
using System.Reflection;

namespace SSMP.Util;

/// <summary>
/// Helpers for reflection-based settings code that needs to treat
/// <see cref="Observable{T}"/> as its wrapped value.
/// </summary>
internal static class ObservableReflection {
    /// <summary>
    /// Returns true for properties that should be included in settings sync.
    /// </summary>
    public static bool IsSyncableProperty(PropertyInfo propertyInfo) {
        if (propertyInfo.GetIndexParameters().Length != 0 || !propertyInfo.CanRead) {
            return false;
        }

        return IsObservableType(propertyInfo.PropertyType) || propertyInfo.CanWrite;
    }

    /// <summary>
    /// Converts Observable&lt;T&gt; to T; leaves other types unchanged.
    /// </summary>
    public static Type UnwrapType(Type type) =>
        IsObservableType(type) ? type.GetGenericArguments()[0] : type;

    /// <summary>
    /// Extracts the inner value from an observable wrapper.
    /// </summary>
    private static object? UnwrapValue(object? value) =>
        value is IObservable observable ? observable.Value : value;

    /// <summary>
    /// Reads a property value and unwraps it when the property is observable.
    /// </summary>
    public static object? GetUnwrappedPropertyValue(PropertyInfo propertyInfo, object target) =>
        UnwrapValue(propertyInfo.GetValue(target, null));

    /// <summary>
    /// Sets an observable's inner value when present, otherwise uses the normal setter.
    /// </summary>
    public static bool TrySetPropertyValue(PropertyInfo propertyInfo, object target, object? value) {
        var currentValue = propertyInfo.GetValue(target, null);
        if (currentValue is IObservable observable) {
            observable.Value = value;
            return true;
        }

        if (!propertyInfo.CanWrite) {
            return false;
        }

        propertyInfo.SetValue(target, value, null);
        return true;
    }

    /// <summary>
    /// Detects closed generic Observable&lt;T&gt; types.
    /// </summary>
    public static bool IsObservableType(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Observable<>);
}
