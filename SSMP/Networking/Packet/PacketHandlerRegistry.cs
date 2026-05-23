using System;
using System.Collections.Generic;
using SSMP.Logging;
using SSMP.Util;

namespace SSMP.Networking.Packet;

/// <summary>
/// Generic registry for packet handlers that eliminates repetitive registration/execution code.
/// Supports both client handlers (no ID parameter) and server handlers (with client ID parameter).
/// </summary>
/// <typeparam name="TPacketId">The enum type for packet IDs.</typeparam>
/// <typeparam name="THandler">The delegate type for packet handlers.</typeparam>
internal class PacketHandlerRegistry<TPacketId, THandler>
    where TPacketId : notnull
    where THandler : Delegate {
    
    /// <summary>
    /// The registered handlers indexed by packet ID.
    /// </summary>
    private readonly Dictionary<TPacketId, THandler> _handlers = new();
    
    /// <summary>
    /// The dispatcher that handles how to call the packet handlers.
    /// For clients, this will be the <see cref="ClientPacketHandlerRegistryDispatcher"/>, which invokes packet
    /// handlers on the Unity main thread.
    /// For servers, this will be the <see cref="ServerPacketHandlerRegistryDispatcher"/>, which directly invokes the
    /// packet handlers.
    /// </summary>
    private readonly IPacketHandlerRegistryDispatcher _dispatcher;
    
    /// <summary>
    /// Descriptive name for logging messages.
    /// </summary>
    private readonly string _registryName;

    /// <summary>
    /// Constructs a new packet handler registry.
    /// </summary>
    /// <param name="registryName">Name for logging purposes (e.g., "client update", "server connection").</param>
    /// <param name="dispatcher">The dispatcher that handles how to call the packet handlers.</param>
    public PacketHandlerRegistry(string registryName, IPacketHandlerRegistryDispatcher dispatcher) {
        _registryName = registryName;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Registers a handler for the given packet ID.
    /// </summary>
    /// <param name="packetId">The packet ID to register the handler for.</param>
    /// <param name="handler">The handler delegate.</param>
    /// <returns>True if registration successful, false if handler already exists.</returns>
    public void Register(TPacketId packetId, THandler handler) {
        if (_handlers.TryAdd(packetId, handler)) return;
        Logger.Warn($"Tried to register already existing {_registryName} packet handler: {packetId}");
    }

    /// <summary>
    /// Deregisters a handler for the given packet ID.
    /// </summary>
    /// <param name="packetId">The packet ID to deregister.</param>
    /// <returns>True if deregistration successful, false if handler didn't exist.</returns>
    public bool Deregister(TPacketId packetId) {
        if (!_handlers.Remove(packetId)) {
            Logger.Warn($"Tried to remove nonexistent {_registryName} packet handler: {packetId}");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Executes a handler for a client packet (no client ID parameter).
    /// </summary>
    /// <param name="packetId">The packet ID.</param>
    /// <param name="invoker">Action that invokes the handler with appropriate parameters.</param>
    /// <returns>True if handler was found and invoked, false otherwise.</returns>
    public void Execute(TPacketId packetId, Action<THandler> invoker) {
        if (!_handlers.TryGetValue(packetId, out var handler)) {
            Logger.Error($"There is no {_registryName} packet handler registered for ID: {packetId}");
            return;
        }

        _dispatcher.Dispatch(() => SafeInvoke(packetId, handler, invoker));
    }

    /// <summary>
    /// Safely invokes a handler with exception handling.
    /// </summary>
    private void SafeInvoke(TPacketId packetId, THandler handler, Action<THandler> invoker) {
        try {
            invoker(handler);
        } catch (Exception e) {
            Logger.Error($"Exception occurred while executing {_registryName} packet handler for ID {packetId}:\n{e}");
        }
    }
}

/// <summary>
/// Interface for packet handler dispatchers.
/// </summary>
internal interface IPacketHandlerRegistryDispatcher {
    void Dispatch(Action action);
}

/// <summary>
/// Implementation of packet handler dispatcher to immediately invoke the handler directly for the server-side.
/// </summary>
internal class ServerPacketHandlerRegistryDispatcher : IPacketHandlerRegistryDispatcher {
    public void Dispatch(Action action) {
        action.Invoke();
    }
}

/// <summary>
/// Implementation of packet handler dispatcher to invoke the handler on Unity's main thread.
/// </summary>
internal class ClientPacketHandlerRegistryDispatcher : IPacketHandlerRegistryDispatcher {
    public void Dispatch(Action action) {
        ThreadUtil.RunActionOnMainThread(action);
    }
}
