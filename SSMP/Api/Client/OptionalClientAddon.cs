using System;

namespace SSMP.Api.Client;

/// <summary>
/// Abstract class for a client addon that can be disabled by the server. Extends <see cref="ClientAddon"/>.
/// </summary>
public abstract class OptionalClientAddon : ClientAddon {
    /// <summary>
    /// Whether this addon is disabled, meaning network is restricted
    /// </summary>
    internal bool DisabledByClient = false;

    /// <summary>
    /// Whether this addon is disabled by the server, meaning network is restricted
    /// </summary>
    internal bool DisabledByServer = false;

    /// <inheritdoc cref="DisabledByClient"/>
    public bool Disabled => DisabledByClient || DisabledByServer;

    /// <summary>
    /// Sets the addon as being disabled by the server or 
    /// </summary>
    /// <param name="disabled"></param>
    internal void SetServerDisabled(bool disabled) {
        var valueChanged = disabled != DisabledByServer && !DisabledByClient;
        DisabledByServer = disabled;

        if (valueChanged) {
            OnStateChanged(disabled);
        }
    }

    /// <summary>
    /// Callback method for when this addon gets enabled.
    /// </summary>
    protected abstract void OnEnable();

    /// <summary>
    /// Callback method for when this addon gets disabled.
    /// </summary>
    protected abstract void OnDisable();

    /// <summary>
    /// Runs <see cref="OnEnable"/> or <see cref="OnDisable"/> when the addon is enabled or disabled.
    /// </summary>
    /// <param name="disabled"></param>
    internal void OnStateChanged(bool disabled) {
        if (disabled) {
            try {
                OnDisable();
            } catch (Exception e) {
                Logger.Error($"Exception was thrown while calling OnDisable for addon '{GetName()}':\n{e}");
            }
        } else {
            try {
                OnEnable();
            } catch (Exception e) {
                Logger.Error($"Exception was thrown while calling OnEnable for addon '{GetName()}':\n{e}");
            }
        }
    }
}
