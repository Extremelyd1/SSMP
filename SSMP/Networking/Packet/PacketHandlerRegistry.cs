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
    /// Whether to dispatch handler invocations to the Unity main thread.
    /// Client handlers typically need main thread dispatch; server handlers do not.
    /// </summary>
    private readonly bool _dispatchToMainThread;
    
    /// <summary>
    /// Descriptive name for logging messages.
    /// </summary>
    private readonly string _registryName;

    /// <summary>
    /// Constructs a new packet handler registry.
    /// </summary>
    /// <param name="registryName">Name for logging purposes (e.g., "client update", "server connection").</param>
    /// <param name="dispatchToMainThread">Whether to dispatch handler calls to Unity main thread.</param>
    public PacketHandlerRegistry(string registryName, bool dispatchToMainThread) {
        _registryName = registryName;
        _dispatchToMainThread = dispatchToMainThread;
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

        if (_dispatchToMainThread) {
            ThreadUtil.RunActionOnMainThread(() => SafeInvoke(packetId, handler, invoker));
        } else {
            SafeInvoke(packetId, handler, invoker);
        }
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
