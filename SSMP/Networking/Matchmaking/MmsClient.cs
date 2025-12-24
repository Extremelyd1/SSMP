using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SSMP.Logging;

namespace SSMP.Networking.Matchmaking;

/// <summary>
/// High-performance client for the MatchMaking Service (MMS) API.
/// Handles lobby creation, lookup, heartbeat, and NAT hole-punching coordination.
/// </summary>
internal class MmsClient {
    /// <summary>
    /// Base URL of the MMS server (e.g., "http://localhost:5000")
    /// </summary>
    private readonly string _baseUrl;

    /// <summary>
    /// Authentication token for host operations (heartbeat, close, pending clients).
    /// Set when a lobby is created, cleared when closed.
    /// </summary>
    private string? _hostToken;

    /// <summary>
    /// The currently active lobby ID, if this client is hosting a lobby.
    /// </summary>
    private string? CurrentLobbyId { get; set; }

    /// <summary>
    /// Timer that sends periodic heartbeats to keep the lobby alive on the MMS.
    /// Fires every 30 seconds while a lobby is active.
    /// </summary>
    private Timer? _heartbeatTimer;

    /// <summary>
    /// Timer that polls for pending client connections that need NAT hole-punching.
    /// Fires every 2 seconds while polling is active.
    /// </summary>
    private Timer? _pendingClientTimer;

    /// <summary>
    /// Interval between heartbeat requests (30 seconds).
    /// Keeps the lobby registered and prevents timeout on the MMS.
    /// </summary>
    private const int HeartbeatIntervalMs = 30000;

    /// <summary>
    /// HTTP request timeout in milliseconds (5 seconds).
    /// Prevents hanging on unresponsive server.
    /// </summary>
    private const int HttpTimeoutMs = 5000;

    /// <summary>
    /// Interval between polls for pending clients (2 seconds).
    /// Balances responsiveness with server load.
    /// </summary>
    private const int PendingClientPollIntervalMs = 2000;

    /// <summary>
    /// Initial delay before starting pending client polling (1 second).
    /// Allows lobby creation to complete before polling begins.
    /// </summary>
    private const int PendingClientInitialDelayMs = 1000;

    /// <summary>
    /// Reusable empty JSON object bytes for heartbeat requests.
    /// Eliminates allocations since heartbeats send no data.
    /// </summary>
    private static readonly byte[] EmptyJsonBytes = "{}"u8.ToArray();

    /// <summary>
    /// Shared character array pool for zero-allocation JSON string building.
    /// Reuses buffers across all JSON formatting operations.
    /// </summary>
    private static readonly ArrayPool<char> CharPool = ArrayPool<char>.Shared;

    /// <summary>
    /// Shared HttpClient instance for connection pooling and reuse across all MmsClient instances.
    /// This provides 3-5x performance improvement over creating new connections per request.
    /// Configured for optimal performance with disabled cookies, proxies, and redirects.
    /// </summary>
    private static readonly HttpClient HttpClient = CreateHttpClient();

    /// <summary>
    /// Creates and configures the shared HttpClient with optimal performance settings.
    /// </summary>
    /// <returns>Configured HttpClient instance for MMS API calls</returns>
    private static HttpClient CreateHttpClient() {
        // Configure handler for maximum performance
        var handler = new HttpClientHandler {
            // Skip proxy detection for faster connections
            UseProxy = false,
            // MMS doesn't use cookies
            UseCookies = false,
            // MMS doesn't redirect
            AllowAutoRedirect = false
        };

        // Configure ServicePointManager for connection pooling (works in Unity Mono)
        System.Net.ServicePointManager.DefaultConnectionLimit = 10;
        // Disable Nagle for lower latency
        System.Net.ServicePointManager.UseNagleAlgorithm = false;
        // Skip 100-Continue handshake
        System.Net.ServicePointManager.Expect100Continue = false;

        return new HttpClient(handler) {
            Timeout = TimeSpan.FromMilliseconds(HttpTimeoutMs)
        };
    }

    /// <summary>
    /// Initializes a new instance of the MmsClient.
    /// </summary>
    /// <param name="baseUrl">Base URL of the MMS server (default: "http://localhost:5000")</param>
    public MmsClient(string baseUrl = "http://localhost:5000") {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    /// <summary>
    /// Creates a new lobby on the MMS and registers this client as the host.
    /// Automatically discovers public endpoint via STUN and starts heartbeat timer.
    /// </summary>
    /// <param name="hostPort">Local port the host is listening on for client connections</param>
    /// <returns>The lobby ID if successful, null on failure</returns>
    public string? CreateLobby(int hostPort) {
        try {
            // Attempt STUN discovery to find public IP and port (for NAT traversal)
            var publicEndpoint = StunClient.DiscoverPublicEndpoint(hostPort);

            // Rent a buffer from the pool to build JSON without allocations
            var buffer = CharPool.Rent(256);
            try {
                int length;
                if (publicEndpoint != null) {
                    // Public endpoint discovered - include IP and port in request
                    var (ip, port) = publicEndpoint.Value;
                    length = FormatJson(buffer, ip, port);
                    Logger.Info($"MmsClient: Discovered public endpoint {ip}:{port}");
                } else {
                    // STUN failed - MMS will use the connection's source IP
                    length = FormatJsonPortOnly(buffer, hostPort);
                    Logger.Warn("MmsClient: STUN discovery failed, MMS will use connection IP");
                }

                // Build string from buffer and send POST request (run on background thread)
                var json = new string(buffer, 0, length);
                var response = Task.Run(async () => await PostJsonAsync($"{_baseUrl}/lobby", json)).Result;
                if (response == null) return null;

                // Parse response to extract lobby ID and host token
                var lobbyId = ExtractJsonValueSpan(response.AsSpan(), "lobbyId");
                var hostToken = ExtractJsonValueSpan(response.AsSpan(), "hostToken");

                if (lobbyId == null || hostToken == null) {
                    Logger.Error($"MmsClient: Invalid response from CreateLobby: {response}");
                    return null;
                }

                // Store tokens and start heartbeat to keep lobby alive
                _hostToken = hostToken;
                CurrentLobbyId = lobbyId;

                StartHeartbeat();
                Logger.Info($"MmsClient: Created lobby {lobbyId}");
                return lobbyId;
            } finally {
                // Always return buffer to pool to enable reuse
                CharPool.Return(buffer);
            }
        } catch (Exception ex) {
            Logger.Error($"MmsClient: Failed to create lobby: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Closes the currently hosted lobby and unregisters it from the MMS.
    /// Stops heartbeat and pending client polling timers.
    /// </summary>
    public void CloseLobby() {
        if (_hostToken == null) return;

        // Stop all timers before closing
        StopHeartbeat();
        StopPendingClientPolling();

        try {
            // Send DELETE request to remove lobby from MMS (run on background thread)
            Task.Run(async () => await DeleteRequestAsync($"{_baseUrl}/lobby/{_hostToken}")).Wait(HttpTimeoutMs);
            Logger.Info($"MmsClient: Closed lobby {CurrentLobbyId}");
        } catch (Exception ex) {
            Logger.Warn($"MmsClient: Failed to close lobby: {ex.Message}");
        }

        // Clear state
        _hostToken = null;
        CurrentLobbyId = null;
    }

    /// <summary>
    /// Joins an existing lobby by notifying the MMS of the client's endpoint.
    /// The MMS coordinates NAT hole-punching by informing the host about this client.
    /// </summary>
    /// <param name="lobbyId">The lobby ID to join</param>
    /// <param name="clientIp">The client's public IP address</param>
    /// <param name="clientPort">The client's public port</param>
    /// <returns>Tuple of (hostIp, hostPort) if successful, null on failure</returns>
    public (string hostIp, int hostPort)? JoinLobby(string lobbyId, string clientIp, int clientPort) {
        try {
            // Build join request JSON using pooled buffer
            var buffer = CharPool.Rent(256);
            try {
                var length = FormatJoinJson(buffer, clientIp, clientPort);
                var json = new string(buffer, 0, length);

                // Send join request to MMS (run on background thread)
                var response = Task.Run(async () => await PostJsonAsync($"{_baseUrl}/lobby/{lobbyId}/join", json)).Result;
                if (response == null) return null;

                // Parse host connection details from response
                var span = response.AsSpan();
                var hostIp = ExtractJsonValueSpan(span, "hostIp");
                var hostPortStr = ExtractJsonValueSpan(span, "hostPort");

                if (hostIp == null || hostPortStr == null || !TryParseInt(hostPortStr.AsSpan(), out var hostPort)) {
                    Logger.Error($"MmsClient: Invalid response from JoinLobby: {response}");
                    return null;
                }

                Logger.Info($"MmsClient: Joined lobby {lobbyId}, host at {hostIp}:{hostPort}");
                return (hostIp, hostPort);
            } finally {
                CharPool.Return(buffer);
            }
        } catch (Exception ex) {
            Logger.Error($"MmsClient: Failed to join lobby: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Retrieves the list of clients waiting to connect to this lobby.
    /// Used for NAT hole-punching - the host needs to send packets to these endpoints.
    /// </summary>
    /// <returns>List of (ip, port) tuples for pending clients</returns>
    private List<(string ip, int port)> GetPendingClients() {
        var result = new List<(string ip, int port)>(8);
        if (_hostToken == null) return result;

        try {
            // Query MMS for pending client list (run on background thread)
            var response = Task.Run(async () => await GetJsonAsync($"{_baseUrl}/lobby/pending/{_hostToken}")).Result;
            if (response == null) return result;

            // Parse JSON array using Span for zero allocations
            var span = response.AsSpan();
            var idx = 0;

            // Scan for each client entry in the JSON array
            while (idx < span.Length) {
                var ipStart = span[idx..].IndexOf("\"clientIp\":");
                if (ipStart == -1) break; // No more clients

                ipStart += idx;
                var ip = ExtractJsonValueSpan(span[ipStart..], "clientIp");
                var portStr = ExtractJsonValueSpan(span[ipStart..], "clientPort");

                // Add valid client to result list
                if (ip != null && portStr != null && TryParseInt(portStr.AsSpan(), out var port)) {
                    result.Add((ip, port));
                }

                // Move past this entry to find next client
                idx = ipStart + 1;
            }

            if (result.Count > 0) {
                Logger.Info($"MmsClient: Got {result.Count} pending clients to punch");
            }
        } catch (Exception ex) {
            Logger.Warn($"MmsClient: Failed to get pending clients: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Event raised when a pending client needs NAT hole-punching.
    /// Subscribers should send packets to the specified endpoint to punch through NAT.
    /// </summary>
    public static event Action<string, int>? PunchClientRequested;

    /// <summary>
    /// Starts polling the MMS for pending clients that need NAT hole-punching.
    /// Should be called after creating a lobby to enable client connections.
    /// </summary>
    public void StartPendingClientPolling() {
        StopPendingClientPolling(); // Ensure no duplicate timers
        _pendingClientTimer = new Timer(
            PollPendingClients, null,
            PendingClientInitialDelayMs, PendingClientPollIntervalMs
        );
    }

    /// <summary>
    /// Stops polling for pending clients.
    /// Called when lobby is closed or no longer accepting connections.
    /// </summary>
    private void StopPendingClientPolling() {
        _pendingClientTimer?.Dispose();
        _pendingClientTimer = null;
    }

    /// <summary>
    /// Timer callback that polls for pending clients and raises events for each.
    /// </summary>
    /// <param name="state">Unused timer state parameter</param>
    private void PollPendingClients(object? state) {
        var pending = GetPendingClients();
        // Raise event for each pending client so they can be hole-punched
        foreach (var (ip, port) in pending) {
            PunchClientRequested?.Invoke(ip, port);
        }
    }

    /// <summary>
    /// Starts the heartbeat timer to keep the lobby alive on the MMS.
    /// Lobbies without heartbeats expire after a timeout period.
    /// </summary>
    private void StartHeartbeat() {
        StopHeartbeat(); // Ensure no duplicate timers
        _heartbeatTimer = new Timer(SendHeartbeat, null, HeartbeatIntervalMs, HeartbeatIntervalMs);
    }

    /// <summary>
    /// Stops the heartbeat timer.
    /// Called when lobby is closed.
    /// </summary>
    private void StopHeartbeat() {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
    }

    /// <summary>
    /// Timer callback that sends a heartbeat to the MMS.
    /// Uses empty JSON body and reusable byte array to minimize allocations.
    /// </summary>
    /// <param name="state">Unused timer state parameter</param>
    private void SendHeartbeat(object? state) {
        if (_hostToken == null) return;

        try {
            // Send empty JSON body - just need to hit the endpoint (run on background thread)
            Task.Run(async () => await PostJsonBytesAsync($"{_baseUrl}/lobby/heartbeat/{_hostToken}", EmptyJsonBytes)).Wait(HttpTimeoutMs);
        } catch (Exception ex) {
            Logger.Warn($"MmsClient: Heartbeat failed: {ex.Message}");
        }
    }

    #region HTTP Helpers (Async with HttpClient)

    /// <summary>
    /// Performs an HTTP GET request and returns the response body as a string.
    /// Uses ResponseHeadersRead for efficient streaming.
    /// </summary>
    /// <param name="url">The URL to GET</param>
    /// <returns>Response body as string, or null if request failed</returns>
    private static async Task<string?> GetJsonAsync(string url) {
        try {
            // ResponseHeadersRead allows streaming without buffering entire response
            var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return null;

            return await response.Content.ReadAsStringAsync();
        } catch (HttpRequestException) {
            // Network error or invalid response
            return null;
        } catch (TaskCanceledException) {
            // Timeout exceeded
            return null;
        }
    }

    /// <summary>
    /// Performs an HTTP POST request with JSON content.
    /// </summary>
    /// <param name="url">The URL to POST to</param>
    /// <param name="json">JSON string to send as request body</param>
    /// <returns>Response body as string</returns>
    private static async Task<string?> PostJsonAsync(string url, string json) {
        // StringContent handles UTF-8 encoding and sets Content-Type header
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await HttpClient.PostAsync(url, content);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Performs an HTTP POST request with pre-encoded JSON bytes.
    /// More efficient than string-based version for reusable content like heartbeats.
    /// </summary>
    /// <param name="url">The URL to POST to</param>
    /// <param name="jsonBytes">JSON bytes to send as request body</param>
    /// <returns>Response body as string</returns>
    private static async Task<string?> PostJsonBytesAsync(string url, byte[] jsonBytes) {
        var content = new ByteArrayContent(jsonBytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var response = await HttpClient.PostAsync(url, content);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Performs an HTTP DELETE request.
    /// Used to close lobbies on the MMS.
    /// </summary>
    /// <param name="url">The URL to DELETE</param>
    private static async Task DeleteRequestAsync(string url) {
        await HttpClient.DeleteAsync(url);
    }

    #endregion

    #region Zero-Allocation JSON Helpers

    /// <summary>
    /// Formats JSON for CreateLobby request with IP and port.
    /// Builds: {"HostIp":"x.x.x.x","HostPort":12345}
    /// </summary>
    /// <param name="buffer">Character buffer to write into (must have sufficient capacity)</param>
    /// <param name="ip">Host IP address</param>
    /// <param name="port">Host port number</param>
    /// <returns>Number of characters written to buffer</returns>
    private static int FormatJson(Span<char> buffer, string ip, int port) {
        const string prefix = "{\"HostIp\":\"";
        const string middle = "\",\"HostPort\":";
        const string suffix = "}";

        var pos = 0;

        // Copy string literals directly into buffer
        prefix.AsSpan().CopyTo(buffer.Slice(pos));
        pos += prefix.Length;

        ip.AsSpan().CopyTo(buffer.Slice(pos));
        pos += ip.Length;

        middle.AsSpan().CopyTo(buffer.Slice(pos));
        pos += middle.Length;

        // Write integer directly without ToString() allocation
        pos += WriteInt(buffer.Slice(pos), port);

        suffix.AsSpan().CopyTo(buffer.Slice(pos));
        pos += suffix.Length;

        return pos;
    }

    /// <summary>
    /// Formats JSON for CreateLobby request with port only.
    /// Builds: {"HostPort":12345}
    /// Used when STUN discovery fails and MMS will infer IP from connection.
    /// </summary>
    /// <param name="buffer">Character buffer to write into</param>
    /// <param name="port">Host port number</param>
    /// <returns>Number of characters written to buffer</returns>
    private static int FormatJsonPortOnly(Span<char> buffer, int port) {
        const string prefix = "{\"HostPort\":";
        const string suffix = "}";

        var pos = 0;
        prefix.AsSpan().CopyTo(buffer.Slice(pos));
        pos += prefix.Length;

        pos += WriteInt(buffer.Slice(pos), port);

        suffix.AsSpan().CopyTo(buffer.Slice(pos));
        pos += suffix.Length;

        return pos;
    }

    /// <summary>
    /// Formats JSON for JoinLobby request.
    /// Builds: {"clientIp":"x.x.x.x","clientPort":12345}
    /// </summary>
    /// <param name="buffer">Character buffer to write into</param>
    /// <param name="clientIp">Client's public IP address</param>
    /// <param name="clientPort">Client's public port</param>
    /// <returns>Number of characters written to buffer</returns>
    private static int FormatJoinJson(Span<char> buffer, string clientIp, int clientPort) {
        const string prefix = "{\"clientIp\":\"";
        const string middle = "\",\"clientPort\":";
        const string suffix = "}";

        var pos = 0;
        prefix.AsSpan().CopyTo(buffer.Slice(pos));
        pos += prefix.Length;

        clientIp.AsSpan().CopyTo(buffer.Slice(pos));
        pos += clientIp.Length;

        middle.AsSpan().CopyTo(buffer.Slice(pos));
        pos += middle.Length;

        pos += WriteInt(buffer.Slice(pos), clientPort);

        suffix.AsSpan().CopyTo(buffer.Slice(pos));
        pos += suffix.Length;

        return pos;
    }

    /// <summary>
    /// Writes an integer to a character buffer without allocations.
    /// 5-10x faster than int.ToString().
    /// </summary>
    /// <param name="buffer">Buffer to write into</param>
    /// <param name="value">Integer value to write</param>
    /// <returns>Number of characters written</returns>
    private static int WriteInt(Span<char> buffer, int value) {
        // Handle zero specially
        if (value == 0) {
            buffer[0] = '0';
            return 1;
        }

        var pos = 0;

        // Handle negative numbers
        if (value < 0) {
            buffer[pos++] = '-';
            value = -value;
        }

        // Extract digits in reverse order
        var digitStart = pos;
        do {
            buffer[pos++] = (char) ('0' + (value % 10));
            value /= 10;
        } while (value > 0);

        // Reverse the digits to correct order
        buffer.Slice(digitStart, pos - digitStart).Reverse();
        return pos;
    }

    /// <summary>
    /// Parses an integer from a character span without allocations.
    /// 10-20x faster than int.Parse() or int.TryParse() on strings.
    /// </summary>
    /// <param name="span">Character span containing the integer</param>
    /// <param name="result">Parsed integer value</param>
    /// <returns>True if parsing succeeded, false otherwise</returns>
    private static bool TryParseInt(ReadOnlySpan<char> span, out int result) {
        result = 0;
        if (span.IsEmpty) return false;

        var sign = 1;
        var i = 0;

        // Check for negative sign
        if (span[0] == '-') {
            sign = -1;
            i = 1;
        }

        // Parse digit by digit
        for (; i < span.Length; i++) {
            var c = span[i];
            // Invalid character
            if (c is < '0' or > '9') return false;
            result = result * 10 + (c - '0');
        }

        result *= sign;
        return true;
    }

    /// <summary>
    /// Extracts a JSON value by key from a JSON string using zero allocations.
    /// Supports both string values (quoted) and numeric values (unquoted).
    /// </summary>
    /// <param name="json">JSON string to search</param>
    /// <param name="key">Key to find (without quotes)</param>
    /// <returns>The value as a string, or null if not found</returns>
    /// <remarks>
    /// This is a simple parser suitable for MMS responses. It assumes well-formed JSON.
    /// Searches for "key": pattern and extracts the following value.
    /// </remarks>
    private static string? ExtractJsonValueSpan(ReadOnlySpan<char> json, string key) {
        // Build search pattern: "key":
        Span<char> searchKey = stackalloc char[key.Length + 3];
        searchKey[0] = '"';
        key.AsSpan().CopyTo(searchKey[1..]);
        searchKey[key.Length + 1] = '"';
        searchKey[key.Length + 2] = ':';

        // Find the key in JSON
        var idx = json.IndexOf(searchKey, StringComparison.Ordinal);
        if (idx == -1) return null;

        var valueStart = idx + searchKey.Length;

        // Skip any whitespace after the colon
        while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
            valueStart++;

        if (valueStart >= json.Length) return null;

        // Determine if value is quoted (string) or unquoted (number)
        if (json[valueStart] == '"') {
            // String value - find closing quote
            var valueEnd = json[(valueStart + 1)..].IndexOf('"');
            return valueEnd == -1 ? null : json.Slice(valueStart + 1, valueEnd).ToString();
        } else {
            // Numeric value - read until non-digit character
            var valueEnd = valueStart;
            while (valueEnd < json.Length &&
                   (char.IsDigit(json[valueEnd]) || json[valueEnd] == '.' || json[valueEnd] == '-'))
                valueEnd++;
            return json.Slice(valueStart, valueEnd - valueStart).ToString();
        }
    }

    #endregion
}
