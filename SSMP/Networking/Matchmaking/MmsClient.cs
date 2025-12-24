using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using SSMP.Logging;

namespace SSMP.Networking.Matchmaking;

/// <summary>
/// Client for the MatchMaking Service (MMS) API.
/// Handles lobby creation, lookup, and heartbeat.
/// </summary>
internal class MmsClient {
    /// <summary>
    /// Base URL of the MMS server.
    /// </summary>
    private readonly string _baseUrl;

    /// <summary>
    /// Current host token (only set when hosting a lobby).
    /// </summary>
    private string? _hostToken;

    /// <summary>
    /// Current lobby ID (only set when hosting).
    /// </summary>
    public string? CurrentLobbyId { get; private set; }

    /// <summary>
    /// Timer for sending heartbeats.
    /// </summary>
    private Timer? _heartbeatTimer;

    /// <summary>
    /// Interval between heartbeats in milliseconds.
    /// </summary>
    private const int HeartbeatIntervalMs = 30000;

    public MmsClient(string baseUrl = "http://localhost:5000") {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    /// <summary>
    /// Creates a new lobby on the MMS server.
    /// Returns the lobby ID, or null on failure.
    /// </summary>
    public string? CreateLobby(int hostPort) {
        try {
            // Discover public endpoint via STUN
            var publicEndpoint = StunClient.DiscoverPublicEndpoint(hostPort);
            string hostIp;
            int publicPort;

            if (publicEndpoint != null) {
                hostIp = publicEndpoint.Value.ip;
                publicPort = publicEndpoint.Value.port;
                Logger.Info($"MmsClient: Discovered public endpoint {hostIp}:{publicPort}");
            } else {
                // Fallback: let MMS use the connection's source IP
                hostIp = "";
                publicPort = hostPort;
                Logger.Warn("MmsClient: STUN discovery failed, MMS will use connection IP");
            }

            var json = string.IsNullOrEmpty(hostIp) 
                ? $"{{\"HostPort\":{publicPort}}}"
                : $"{{\"HostIp\":\"{hostIp}\",\"HostPort\":{publicPort}}}";
            
            var response = PostJson($"{_baseUrl}/lobby", json);
            
            if (response == null) return null;

            // Parse response: {"lobbyId":"XXXX-XXXX","hostToken":"..."}
            var lobbyId = ExtractJsonValue(response, "lobbyId");
            var hostToken = ExtractJsonValue(response, "hostToken");

            if (lobbyId == null || hostToken == null) {
                Logger.Error($"MmsClient: Invalid response from CreateLobby: {response}");
                return null;
            }

            _hostToken = hostToken;
            CurrentLobbyId = lobbyId;
            
            // Start heartbeat
            StartHeartbeat();

            Logger.Info($"MmsClient: Created lobby {lobbyId}");
            return lobbyId;
        } catch (Exception ex) {
            Logger.Error($"MmsClient: Failed to create lobby: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Looks up a lobby by ID and returns host info.
    /// Returns (hostIp, hostPort) or null on failure.
    /// </summary>
    public (string hostIp, int hostPort)? LookupLobby(string lobbyId) {
        try {
            var response = GetJson($"{_baseUrl}/lobby/{lobbyId}");
            
            if (response == null) return null;

            var hostIp = ExtractJsonValue(response, "hostIp");
            var hostPortStr = ExtractJsonValue(response, "hostPort");

            if (hostIp == null || hostPortStr == null || !int.TryParse(hostPortStr, out var hostPort)) {
                Logger.Error($"MmsClient: Invalid response from LookupLobby: {response}");
                return null;
            }

            Logger.Info($"MmsClient: Lobby {lobbyId} -> {hostIp}:{hostPort}");
            return (hostIp, hostPort);
        } catch (Exception ex) {
            Logger.Error($"MmsClient: Failed to lookup lobby: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Closes the current lobby (if hosting).
    /// </summary>
    public void CloseLobby() {
        if (_hostToken == null) return;

        StopHeartbeat();
        StopPendingClientPolling();

        try {
            DeleteRequest($"{_baseUrl}/lobby/{_hostToken}");
            Logger.Info($"MmsClient: Closed lobby {CurrentLobbyId}");
        } catch (Exception ex) {
            Logger.Warn($"MmsClient: Failed to close lobby: {ex.Message}");
        }

        _hostToken = null;
        CurrentLobbyId = null;
    }

    /// <summary>
    /// Joins a lobby by registering client's public endpoint.
    /// Returns host info or null on failure.
    /// </summary>
    public (string hostIp, int hostPort)? JoinLobby(string lobbyId, string clientIp, int clientPort) {
        try {
            var json = $"{{\"clientIp\":\"{clientIp}\",\"clientPort\":{clientPort}}}";
            var response = PostJson($"{_baseUrl}/lobby/{lobbyId}/join", json);
            
            if (response == null) return null;

            var hostIp = ExtractJsonValue(response, "hostIp");
            var hostPortStr = ExtractJsonValue(response, "hostPort");

            if (hostIp == null || hostPortStr == null || !int.TryParse(hostPortStr, out var hostPort)) {
                Logger.Error($"MmsClient: Invalid response from JoinLobby: {response}");
                return null;
            }

            Logger.Info($"MmsClient: Joined lobby {lobbyId}, host at {hostIp}:{hostPort}");
            return (hostIp, hostPort);
        } catch (Exception ex) {
            Logger.Error($"MmsClient: Failed to join lobby: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets pending clients that need punch-back (host calls this).
    /// </summary>
    public List<(string ip, int port)> GetPendingClients() {
        var result = new List<(string ip, int port)>();
        if (_hostToken == null) return result;

        try {
            var response = GetJson($"{_baseUrl}/lobby/pending/{_hostToken}");
            if (response == null) return result;

            // Parse JSON array: [{"clientIp":"...","clientPort":...}, ...]
            // Simple parsing for array of objects
            var idx = 0;
            while (true) {
                var ipStart = response.IndexOf("\"clientIp\":", idx, StringComparison.Ordinal);
                if (ipStart == -1) break;
                
                var ip = ExtractJsonValue(response.Substring(ipStart), "clientIp");
                var portStr = ExtractJsonValue(response.Substring(ipStart), "clientPort");
                
                if (ip != null && portStr != null && int.TryParse(portStr, out var port)) {
                    result.Add((ip, port));
                }
                
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
    /// Event fired when a pending client needs punch-back.
    /// </summary>
    public event Action<string, int>? PendingClientReceived;

    /// <summary>
    /// Timer for polling pending clients.
    /// </summary>
    private Timer? _pendingClientTimer;

    /// <summary>
    /// Starts polling for pending clients (call after creating lobby).
    /// </summary>
    public void StartPendingClientPolling() {
        StopPendingClientPolling();
        _pendingClientTimer = new Timer(PollPendingClients, null, 1000, 2000); // Poll every 2s
    }

    /// <summary>
    /// Stops polling for pending clients.
    /// </summary>
    public void StopPendingClientPolling() {
        _pendingClientTimer?.Dispose();
        _pendingClientTimer = null;
    }

    private void PollPendingClients(object? state) {
        var pending = GetPendingClients();
        foreach (var (ip, port) in pending) {
            PendingClientReceived?.Invoke(ip, port);
        }
    }

    /// <summary>
    /// Starts the heartbeat timer.
    /// </summary>
    private void StartHeartbeat() {
        StopHeartbeat();
        _heartbeatTimer = new Timer(SendHeartbeat, null, HeartbeatIntervalMs, HeartbeatIntervalMs);
    }

    /// <summary>
    /// Stops the heartbeat timer.
    /// </summary>
    private void StopHeartbeat() {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
    }

    /// <summary>
    /// Sends a heartbeat to keep the lobby alive.
    /// </summary>
    private void SendHeartbeat(object? state) {
        if (_hostToken == null) return;

        try {
            PostJson($"{_baseUrl}/lobby/heartbeat/{_hostToken}", "{}");
        } catch (Exception ex) {
            Logger.Warn($"MmsClient: Heartbeat failed: {ex.Message}");
        }
    }

    #region HTTP Helpers

    private static string? GetJson(string url) {
        var request = (HttpWebRequest) WebRequest.Create(url);
        request.Method = "GET";
        request.ContentType = "application/json";
        request.Timeout = 5000;

        try {
            using var response = (HttpWebResponse) request.GetResponse();
            using var reader = new StreamReader(response.GetResponseStream());
            return reader.ReadToEnd();
        } catch (WebException ex) when (ex.Response is HttpWebResponse { StatusCode: HttpStatusCode.NotFound }) {
            return null;
        }
    }

    private static string? PostJson(string url, string json) {
        var request = (HttpWebRequest) WebRequest.Create(url);
        request.Method = "POST";
        request.ContentType = "application/json";
        request.Timeout = 5000;

        var bytes = Encoding.UTF8.GetBytes(json);
        request.ContentLength = bytes.Length;

        using (var stream = request.GetRequestStream()) {
            stream.Write(bytes, 0, bytes.Length);
        }

        using var response = (HttpWebResponse) request.GetResponse();
        using var reader = new StreamReader(response.GetResponseStream());
        return reader.ReadToEnd();
    }

    private static void DeleteRequest(string url) {
        var request = (HttpWebRequest) WebRequest.Create(url);
        request.Method = "DELETE";
        request.Timeout = 5000;

        using var response = (HttpWebResponse) request.GetResponse();
    }

    /// <summary>
    /// Simple JSON value extractor (avoids needing JSON library).
    /// </summary>
    private static string? ExtractJsonValue(string json, string key) {
        var searchKey = $"\"{key}\":";
        var idx = json.IndexOf(searchKey, StringComparison.Ordinal);
        if (idx == -1) return null;

        var valueStart = idx + searchKey.Length;
        
        // Skip whitespace
        while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart])) valueStart++;
        
        if (valueStart >= json.Length) return null;

        // Check if value is a string (quoted) or number
        if (json[valueStart] == '"') {
            var valueEnd = json.IndexOf('"', valueStart + 1);
            if (valueEnd == -1) return null;
            return json.Substring(valueStart + 1, valueEnd - valueStart - 1);
        } else {
            // Number or other unquoted value
            var valueEnd = valueStart;
            while (valueEnd < json.Length && (char.IsDigit(json[valueEnd]) || json[valueEnd] == '.')) valueEnd++;
            return json.Substring(valueStart, valueEnd - valueStart);
        }
    }

    #endregion
}
