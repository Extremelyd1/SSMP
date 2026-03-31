using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using SSMP.Serialization;

namespace SSMP.Util;

/// <summary>
/// A wrapper for a value that tracks changes from an original baseline and provides events for when the value is modified.
/// This class implements <see cref="IObservable"/> to allow for non-generic change tracking at the collection level.
/// </summary>
/// <typeparam name="T">The type of the underlying value to track.</typeparam>
[JsonConverter(typeof(ObservableConverter))]
public sealed class Observable<T> : IObservable {
    private T _value;
    private T _original;

    /// <summary>
    /// Event triggered whenever the value is changed.
    /// Passes the new value to the subscribers.
    /// </summary>
    public event Action<T>? OnChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="Observable{T}"/> class with the specified initial value.
    /// Both the current value and the original baseline are set to the initial value.
    /// </summary>
    /// <param name="initialValue">The initial value to track.</param>
    public Observable(T initialValue) {
        _value = initialValue;
        _original = initialValue;
    }

    /// <summary>
    /// Gets or sets the current value of the observable.
    /// Setting a value that is different from the current value triggers the <see cref="OnChanged"/> event.
    /// </summary>
    public T Value {
        get => _value;
        set {
            if (EqualityComparer<T>.Default.Equals(_value, value)) {
                return;
            }

            _value = value;
            OnChanged?.Invoke(value);
        }
    }

    /// <summary>
    /// Gets a value indicating whether the current value has been modified from its original baseline.
    /// </summary>
    public bool IsModified => !EqualityComparer<T>.Default.Equals(_value, _original);

    /// <summary>
    /// Resets the original baseline to the current value, clearing the <see cref="IsModified"/> status.
    /// </summary>
    public void AcceptChanges() {
        _original = _value;
    }

    /// <inheritdoc />
    object? IObservable.Value {
        get => Value;
        set => Value = (T) value!;
    }

    /// <summary>
    /// Implicitly converts an <see cref="Observable{T}"/> instance to its underlying value of type <typeparamref name="T"/>.
    /// This allows for transparent reading of the value in most contexts.
    /// </summary>
    /// <param name="observable">The observable instance to convert.</param>
    public static implicit operator T(Observable<T> observable) => observable._value;

    /// <summary>
    /// Returns the string representation of the underlying value.
    /// </summary>
    /// <returns>The string representation of the value, or null if the value is null.</returns>
    public override string? ToString() => _value?.ToString();
}
