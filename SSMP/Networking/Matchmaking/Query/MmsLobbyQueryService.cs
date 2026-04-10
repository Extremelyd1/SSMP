using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SSMP.Logging;
using SSMP.Networking.Matchmaking.Parsing;
using SSMP.Networking.Matchmaking.Protocol;
using SSMP.Networking.Matchmaking.Transport;

namespace SSMP.Networking.Matchmaking.Query;

/// <summary>
/// Handles non-host MMS queries: joining an existing lobby, browsing public lobbies,
/// and probing server compatibility before attempting matchmaking.
/// </summary>
internal sealed class MmsLobbyQueryService {
    /// <summary>Base HTTP URL of the MMS server (e.g. <c>https://mms.example.com</c>).</summary>
    private readonly string _baseUrl;

    /// <summary>
    /// Initializes a new <see cref="MmsLobbyQueryService"/>.
    /// </summary>
    /// <param name="baseUrl">Base HTTP URL of the MMS server.</param>
    public MmsLobbyQueryService(string baseUrl) {
        _baseUrl = baseUrl;
    }

    /// <summary>
    /// Sends a join request for <paramref name="lobbyId"/> to MMS, advertising
    /// the client's local UDP port so MMS can facilitate NAT hole-punching.
    /// </summary>
    /// <param name="lobbyId">The MMS lobby identifier to join.</param>
    /// <param name="clientPort">The local UDP port this client is listening on.</param>
    /// <returns>
    /// A <see cref="JoinLobbyResult"/> containing the lobby type, connection data,
    /// and join ID needed for the subsequent WebSocket rendezvous, or <c>null</c>
    /// if the request failed or the response could not be parsed.
    /// </returns>
    public async Task<(JoinLobbyResult? result, MatchmakingError error)>
        JoinLobbyAsync(string lobbyId, int clientPort) {
        var response = await MmsHttpClient.PostJsonAsync(
            $"{_baseUrl}{MmsRoutes.LobbyJoin(lobbyId)}",
            BuildJoinRequestJson(clientPort)
        );
        if (!response.Success || response.Body == null)
            return (null, response.Error);

        return (ParseAndLogJoinResult(lobbyId, response.Body), MatchmakingError.None);
    }

    /// <summary>
    /// Retrieves the list of currently open public lobbies from MMS, optionally
    /// filtered by lobby type.
    /// </summary>
    /// <param name="lobbyType">
    /// When non-<c>null</c>, restricts results to lobbies of the specified
    /// <see cref="PublicLobbyType"/>. When <c>null</c>, all public lobbies are returned.
    /// Matchmaking lobbies are additionally filtered by <see cref="MmsProtocol.CurrentVersion"/>.
    /// </param>
    /// <returns>
    /// A list of <see cref="PublicLobbyInfo"/> entries, or <c>null</c> if the
    /// request failed or the response could not be parsed.
    /// </returns>
    public async Task<(List<PublicLobbyInfo>? lobbies, MatchmakingError error)> GetPublicLobbiesAsync(
        PublicLobbyType? lobbyType = null
    ) {
        var url = BuildPublicLobbiesUrl(lobbyType);
        var response = await MmsHttpClient.GetAsync(url);
        return !response.Success || response.Body == null
            ? (null, response.Error)
            : (MmsResponseParser.ParsePublicLobbies(response.Body), MatchmakingError.None);
    }

    /// <summary>
    /// Probes the MMS server's health endpoint to verify that the client and server
    /// are running a compatible protocol version.
    /// </summary>
    /// <returns>
    /// A tuple of:
    /// <list type="bullet">
    ///   <item>
    ///     <term><c>isCompatible</c></term>
    ///     <description>
    ///       <c>true</c> if the server version matches <see cref="MmsProtocol.CurrentVersion"/>;
    ///       <c>false</c> if a version mismatch was detected;
    ///       <c>null</c> if the server could not be reached or returned an unparseable response.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term><c>error</c></term>
    ///     <description>
    ///       <see cref="MatchmakingError.None"/> on success or unreachable server;
    ///       <see cref="MatchmakingError.NetworkFailure"/> if the response lacked a valid version field;
    ///       <see cref="MatchmakingError.UpdateRequired"/> if the protocol versions differ.
    ///     </description>
    ///   </item>
    /// </list>
    /// </returns>
    public async Task<(bool? isCompatible, MatchmakingError error)> ProbeMatchmakingCompatibilityAsync() {
        var response = await MmsHttpClient.GetAsync($"{_baseUrl}{MmsRoutes.Root}");
        if (!response.Success || response.Body == null)
            return (null, response.Error);

        if (!TryParseServerVersion(response.Body, out var serverVersion))
            return (null, MatchmakingError.NetworkFailure);

        return CheckVersionCompatibility(serverVersion);
    }

    /// <summary>
    /// Builds the JSON request body for a lobby join, advertising the client's
    /// local port and protocol version. <c>ClientIp</c> is sent as <c>null</c>;
    /// MMS infers the external IP from the incoming socket address.
    /// </summary>
    /// <param name="clientPort">Local UDP port the client is listening on.</param>
    /// <returns>A JSON string ready to POST to the join endpoint.</returns>
    private static string BuildJoinRequestJson(int clientPort) =>
        $"{{\"{MmsFields.ClientIpRequest}\":null," +
        $"\"{MmsFields.ClientPortRequest}\":{clientPort}," +
        $"\"{MmsFields.MatchmakingVersionRequest}\":{MmsProtocol.CurrentVersion}}}";

    /// <summary>
    /// Parses the join response and logs the outcome. Returns the result on
    /// success or logs an error and returns <c>null</c> on parse failure.
    /// </summary>
    /// <param name="lobbyId">Lobby ID, used only for log messages.</param>
    /// <param name="response">Raw JSON response body from the join endpoint.</param>
    /// <returns>A populated <see cref="JoinLobbyResult"/>, or <c>null</c> if parsing failed.</returns>
    private static JoinLobbyResult? ParseAndLogJoinResult(string lobbyId, string response) {
        var joinResult = MmsResponseParser.ParseJoinLobbyResult(response);
        if (joinResult == null) {
            Logger.Error(
                $"MmsLobbyQueryService: invalid JoinLobby response (length={response.Length}, hasJoinId={response.Contains(MmsFields.JoinId)}, hasConnectionData={response.Contains(MmsFields.ConnectionData)})"
            );
            return null;
        }

        Logger.Info($"MmsLobbyQueryService: joined lobby {lobbyId}, type={joinResult.LobbyType}");
        return joinResult;
    }

    /// <summary>
    /// Extracts and parses the <c>version</c> field from an MMS health response.
    /// Logs a warning and returns <c>false</c> if the field is absent or non-numeric.
    /// </summary>
    /// <param name="response">Raw JSON health response body.</param>
    /// <param name="serverVersion">Receives the parsed version number on success; 0 on failure.</param>
    /// <returns><c>true</c> if a valid integer version was found; <c>false</c> otherwise.</returns>
    private static bool TryParseServerVersion(string response, out int serverVersion) {
        var versionString = MmsJsonParser.ExtractValue(response.AsSpan(), MmsFields.Version);
        if (int.TryParse(versionString, out serverVersion)) return true;

        Logger.Warn("MmsLobbyQueryService: MMS health response did not include a valid protocol version");
        return false;
    }

    /// <summary>
    /// Compares <paramref name="serverVersion"/> against
    /// <see cref="MmsProtocol.CurrentVersion"/> and returns the appropriate
    /// compatibility result. Logs a warning on mismatch.
    /// </summary>
    /// <param name="serverVersion">Protocol version reported by the MMS health endpoint.</param>
    /// <returns>
    /// <c>(true, None)</c> if versions match;
    /// <c>(false, UpdateRequired)</c> if they differ.
    /// </returns>
    private static (bool? isCompatible, MatchmakingError error) CheckVersionCompatibility(int serverVersion) {
        if (serverVersion == MmsProtocol.CurrentVersion)
            return (true, MatchmakingError.None);

        Logger.Warn(
            $"MmsLobbyQueryService: MMS protocol mismatch " +
            $"(client={MmsProtocol.CurrentVersion}, server={serverVersion})"
        );
        return (false, MatchmakingError.UpdateRequired);
    }

    /// <summary>
    /// Builds the public lobbies query URL, appending a <c>type</c> filter when
    /// <paramref name="lobbyType"/> is specified and a <c>matchmakingVersion</c>
    /// parameter when the type is <see cref="PublicLobbyType.Matchmaking"/>.
    /// </summary>
    /// <param name="lobbyType">
    /// Optional lobby type filter. <c>null</c> returns the unfiltered lobbies URL.
    /// </param>
    /// <returns>The fully constructed URL string ready for an HTTP GET request.</returns>
    private string BuildPublicLobbiesUrl(PublicLobbyType? lobbyType) {
        var url = $"{_baseUrl}{MmsRoutes.Lobbies}";
        if (lobbyType == null) return url;

        url += $"?{MmsQueryKeys.Type}={lobbyType.ToString()!.ToLowerInvariant()}";
        if (lobbyType == PublicLobbyType.Matchmaking)
            url += $"&{MmsQueryKeys.MatchmakingVersion}={MmsProtocol.CurrentVersion}";

        return url;
    }
}
