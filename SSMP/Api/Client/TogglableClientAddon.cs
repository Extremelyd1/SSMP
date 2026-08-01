using System;

namespace SSMP.Api.Client;

/// <summary>
/// Abstract class for a client addon that can be toggled. Extends <see cref="OptionalClientAddon"/>.
/// </summary>
public abstract class TogglableClientAddon : OptionalClientAddon {
    internal void SetClientDisabled(bool disabled) {
        var valueChanged = disabled != DisabledByClient && !DisabledByServer;
        DisabledByClient = disabled;

        if (valueChanged) {
            OnStateChanged(disabled);
        }
    }
}
