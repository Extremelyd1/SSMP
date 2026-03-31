using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SSMP.Util;

/// <summary>
/// Base class for settings objects that automatically discovers and tracks all
/// <see cref="Observable{T}"/> fields and properties declared on concrete subclasses.
/// Reflection runs only once per concrete type; all per-instance and per-event-fire
/// paths are allocation-free.
/// </summary>
public abstract class ObservableBase {
    /// <summary>
    /// All <see cref="IObservable"/> instances discovered on this concrete instance,
    /// used for bulk <see cref="IsModified"/> checks and <see cref="AcceptChanges"/> sweeps.
    /// </summary>
    private readonly List<IObservable> _managedObservables = [];

    /// <summary>
    /// Maps each concrete subclass type to the set of <see cref="Observable{T}"/> members
    /// discovered via reflection. Written once per type under <see cref="MemberCacheLock"/>;
    /// safe for concurrent reads thereafter.
    /// </summary>
    private static readonly Dictionary<Type, MemberInfo[]> MemberCache = [];

    /// <summary>
    /// Guards the one-time write to <see cref="MemberCache"/> for each new concrete type.
    /// </summary>
    private static readonly object MemberCacheLock = new();

    /// <summary>
    /// Raised whenever any tracked <see cref="Observable{T}"/> member changes.
    /// The argument is the member's resolved name (from <see cref="SettingAliasAttribute"/> or
    /// the member name itself).
    /// </summary>
    public event Action<string>? OnChanged;

    /// <summary>
    /// Initializes the instance by discovering and subscribing to all
    /// <see cref="Observable{T}"/> members on the concrete type.
    /// </summary>
    protected ObservableBase() {
        InitializeObservables();
    }

    /// <summary>
    /// Scans the concrete type for <see cref="Observable{T}"/> fields and properties,
    /// caches the member list per type, then subscribes to each member's change event.
    /// </summary>
    private void InitializeObservables() {
        var type = GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        MemberInfo[] members;
        lock (MemberCacheLock) {
            if (!MemberCache.TryGetValue(type, out members!)) {
                var fields = type.GetFields(flags);
                var properties = type.GetProperties(flags);
                var result = new List<MemberInfo>(fields.Length + properties.Length);

                result.AddRange(fields.Where(f => IsObservableType(f.FieldType)));
                result.AddRange(properties.Where(IsObservableProperty));

                var seen = new HashSet<string>(result.Count);
                // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
                foreach (var m in result) {
                    var alias = m.GetCustomAttribute<SettingAliasAttribute>();
                    var resolvedName = alias?.PropertyName ?? m.Name;
                    if (!seen.Add(resolvedName)) {
                        throw new InvalidOperationException(
                            $"{type.Name} declares two observable members that both resolve to the name \"{resolvedName}\". " +
                            "Use [SettingAlias] to assign distinct names.");
                    }
                }

                members = result.ToArray();
                MemberCache[type] = members;
            }
        }

        foreach (var member in members) {
            InitializeMember(member);
        }
    }

    /// <summary>
    /// Resolves the <see cref="IObservable"/> instance and its reported name for a given member,
    /// then wires it to <see cref="OnChanged"/>.
    /// </summary>
    /// <param name="member">The reflected field or property to initialize.</param>
    private void InitializeMember(MemberInfo member) {
        Type memberType;
        IObservable? observable;

        if (member is FieldInfo fi) {
            memberType = fi.FieldType;
            observable = fi.GetValue(this) as IObservable;
        } else {
            var pi = (PropertyInfo) member;
            memberType = pi.PropertyType;
            observable = pi.GetValue(this) as IObservable;
        }

        if (observable == null) {
            return;
        }

        var alias = member.GetCustomAttribute<SettingAliasAttribute>();
        var name = alias?.PropertyName ?? member.Name;

        Subscribe(observable, name, memberType);
        _managedObservables.Add(observable);
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="t"/> is a closed or open construction of
    /// <see cref="Observable{T}"/>.
    /// </summary>
    /// <param name="t">The type to test.</param>
    private static bool IsObservableType(Type t) =>
        t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Observable<>);

    /// <summary>
    /// Returns <c>true</c> if <paramref name="p"/> is a non-indexed property of type
    /// <see cref="Observable{T}"/>.
    /// </summary>
    /// <param name="p">The property to test.</param>
    private static bool IsObservableProperty(PropertyInfo p) =>
        p.GetIndexParameters().Length == 0 && IsObservableType(p.PropertyType);

    /// <summary>
    /// Wires a <see cref="ChangeHandlerWrapper{T}"/> to the member's <c>OnChanged</c> event.
    /// The wrapper closes over <c>(parent, name)</c> and exposes a concrete <c>Handle(T)</c>
    /// method; <see cref="Delegate.CreateDelegate(Type, object, MethodInfo)"/> binds to it directly, producing a plain
    /// virtual call on each event fire.
    /// </summary>
    /// <param name="observable">The <see cref="IObservable"/> instance to subscribe to.</param>
    /// <param name="name">The resolved member name surfaced through <see cref="OnChanged"/>.</param>
    /// <param name="memberType">The closed generic type of the member, used to reflect its event and construct the wrapper.</param>
    private void Subscribe(IObservable observable, string name, Type memberType) {
        var innerType = memberType.GetGenericArguments()[0];
        var eventInfo = memberType.GetEvent("OnChanged")!;
        var actionType = typeof(Action<>).MakeGenericType(innerType);

        var wrapperType = typeof(ChangeHandlerWrapper<>).MakeGenericType(innerType);
        var wrapper = Activator.CreateInstance(wrapperType, this, name)!;
        var handleMethod = wrapperType.GetMethod(nameof(ChangeHandlerWrapper<>.Handle))!;
        var handler = Delegate.CreateDelegate(actionType, wrapper, handleMethod);

        eventInfo.AddEventHandler(observable, handler);
    }

    /// <summary>
    /// Closes over <c>(parent, name)</c> so the delegate produced from <c>Handle</c> is a
    /// plain bound-method call. The new value is intentionally discarded -
    /// <see cref="OnChanged"/> surfaces only the member name.
    /// </summary>
    /// <typeparam name="T">The value type of the <see cref="Observable{T}"/> being watched.</typeparam>
    /// <param name="parent">The <see cref="ObservableBase"/> instance that owns the subscription.</param>
    /// <param name="name">The resolved member name to forward to <see cref="ObservableBase.OnChanged"/>.</param>
    private sealed class ChangeHandlerWrapper<T>(ObservableBase parent, string name) {
        /// <summary>
        /// Invoked by the <see cref="Observable{T}.OnChanged"/> event; forwards the member
        /// name to <see cref="ObservableBase.OnChanged"/>. The new value is intentionally ignored.
        /// </summary>
        /// <param name="_">The new value; unused.</param>
        public void Handle(T _) => parent.OnChanged?.Invoke(name);
    }

    /// <summary>
    /// Returns <c>true</c> if any tracked observable has been modified since the last
    /// <see cref="AcceptChanges"/> call.
    /// </summary>
    public bool IsModified {
        get {
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var i = 0; i < _managedObservables.Count; i++) {
                if (_managedObservables[i].IsModified) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Resets the original baseline for all tracked observables.
    /// </summary>
    public void AcceptChanges() {
        foreach (var o in _managedObservables)
            o.AcceptChanges();
    }
}

/// <summary>
/// Attribute used to mark a field or property as an observable member and explicitly map it to a
/// change-event name. When absent, the name is derived automatically from the member name.
/// </summary>
/// <param name="propertyName">The name to surface in <see cref="ObservableBase.OnChanged"/>.</param>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public abstract class SettingAliasAttribute(string propertyName) : Attribute {
    /// <summary>
    /// The name surfaced through <see cref="ObservableBase.OnChanged"/> in place of the
    /// member's declared name.
    /// </summary>
    public string PropertyName { get; } = propertyName;
}

/// <summary>
/// Non-generic contract implemented by <see cref="Observable{T}"/> to allow uniform state
/// management in <see cref="ObservableBase"/> without per-call reflection or boxing.
/// </summary>  
internal interface IObservable {
    /// <summary>
    /// Gets or sets the underlying value as an object.
    /// </summary>
    object? Value { get; set; }

    /// <summary>
    /// <c>true</c> if the value has changed since the last <see cref="AcceptChanges"/> call.
    /// </summary>
    bool IsModified { get; }

    /// <summary>
    /// Snapshots the current value as the new baseline, clearing <see cref="IsModified"/>.
    /// </summary>
    void AcceptChanges();
}
