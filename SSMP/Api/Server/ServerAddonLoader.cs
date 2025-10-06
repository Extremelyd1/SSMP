using System.Collections.Generic;
using System.IO;
using System.Reflection;
using SSMP.Api.Addon;

namespace SSMP.Api.Server;

/// <summary>
/// Addon loader for the server-side.
/// </summary>
internal class ServerAddonLoader : AddonLoader {
    /// <summary>
    /// Loads all server addons.
    /// </summary>
    /// <returns>A list of ServerAddon instances.</returns>
    public List<ServerAddon> LoadAddons() {
        return LoadAddons<ServerAddon>();
    }
}
