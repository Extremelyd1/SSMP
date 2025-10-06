using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using SSMP.Api.Addon;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Api.Client;

/// <summary>
/// Addon loader for the client-side.
/// </summary>
internal class ClientAddonLoader : AddonLoader {
    /// <summary>
    /// Loads all client addons.
    /// </summary>
    /// <returns>A list of ClientAddon instances.</returns>
    public List<ClientAddon> LoadAddons() {
        return LoadAddons<ClientAddon>();
    }
}
