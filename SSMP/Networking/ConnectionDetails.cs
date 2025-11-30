using System.Collections.Generic;
using SSMP.Networking.Transport.Common;

namespace SSMP.Networking;

/// <summary>
/// Struct containing details required to establish a connection.
/// </summary>
public struct ConnectionDetails {
    /// <summary>
    /// The IP address or Steam ID to connect to.
    /// </summary>
    public string Address;

    /// <summary>
    /// The port to connect to (for UDP).
    /// </summary>
    public int Port;

    /// <summary>
    /// The username of the player connecting.
    /// </summary>
    public string Username;

    /// <summary>
    /// The transport type to use for the connection.
    /// </summary>
    public TransportType TransportType;

    /// <summary>
    /// The authentication key to use for the connection.
    /// </summary>
    public string? AuthKey;

    /// <summary>
    /// Additional properties for the connection (e.g. Lobby ID).
    /// </summary>
    public Dictionary<string, object> Properties;

    public ConnectionDetails(string address, int port, string username, TransportType transportType) {
        Address = address;
        Port = port;
        Username = username;
        TransportType = transportType;
        AuthKey = null;
        Properties = new Dictionary<string, object>();
    }
}
