using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using SSMP.Networking.Matchmaking.Parsing;
using SSMP.Networking.Matchmaking.Protocol;

namespace SSMP.Networking.Matchmaking.Transport;

/// <summary>HTTP transport for MMS; shared <see cref="HttpClient"/> for pool reuse.</summary>
internal static class MmsHttpClient {
    /// <summary>Shared HTTP client instance for connection pooling.</summary>
    private static readonly HttpClient Http = CreateHttpClient();

    static MmsHttpClient() {
        // Note: ProcessExit only fires on graceful shutdown. 
        // Hard crashes will bypass this, meaning the OS will clean up the socket.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Http.Dispose();
    }

    /// <summary>
    /// Performs a GET request to the specified URL.
    /// </summary>
    public static async Task<MmsHttpResponse> GetAsync(string url) {
        try {
            using var response = await Http.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            return new MmsHttpResponse(
                response.IsSuccessStatusCode,
                body,
                InspectErrorBody(response.StatusCode, body)
            );
        } catch (Exception ex) when (IsTransient(ex)) {
            return new MmsHttpResponse(false, null, MatchmakingError.NetworkFailure);
        }
    }

    /// <summary>
    /// Performs a POST request with a JSON body to the specified URL.
    /// </summary>
    public static async Task<MmsHttpResponse> PostJsonAsync(string url, string json) {
        try {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(url, content);
            var body = await response.Content.ReadAsStringAsync();
            return new MmsHttpResponse(
                response.IsSuccessStatusCode,
                body,
                InspectErrorBody(response.StatusCode, body)
            );
        } catch (Exception ex) when (IsTransient(ex)) {
            return new MmsHttpResponse(false, null, MatchmakingError.NetworkFailure);
        }
    }

    /// <summary>Returns HTTP metadata and classified matchmaking errors without throwing.</summary>
    public static async Task<MmsHttpResponse> DeleteAsync(string url) {
        try {
            using var response = await Http.DeleteAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            return new MmsHttpResponse(
                response.IsSuccessStatusCode,
                body,
                InspectErrorBody(response.StatusCode, body)
            );
        } catch (Exception ex) when (IsTransient(ex)) {
            return new MmsHttpResponse(false, null, MatchmakingError.NetworkFailure);
        }
    }

    /// <summary>
    /// Checks the response body for MMS-specific error codes.
    /// </summary>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="body">The response body.</param>
    private static MatchmakingError InspectErrorBody(HttpStatusCode status, string? body) {
        if ((int) status < 400 || body == null) return MatchmakingError.None;

        var errorCode = MmsJsonParser.ExtractValue(body.AsSpan(), MmsFields.ErrorCode);
        return errorCode == MmsProtocol.UpdateRequiredErrorCode
            ? MatchmakingError.UpdateRequired
            : MatchmakingError.NetworkFailure;
    }

    /// <summary>
    /// Determines if an exception represents a transient network issue.
    /// </summary>
    /// <param name="ex">The exception to check.</param>
    /// <returns><c>true</c> if transient; otherwise, <c>false</c>.</returns>
    private static bool IsTransient(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException;

    /// <summary>
    /// Configures and returns an optimized <see cref="HttpClient"/> instance.
    /// </summary>
    /// <returns>A new <see cref="HttpClient"/> instance.</returns>
    private static HttpClient CreateHttpClient() {
        var handler = new HttpClientHandler {
            UseProxy = false,
            UseCookies = false,
            AllowAutoRedirect = false
        };

        var client = new HttpClient(handler) {
            Timeout = TimeSpan.FromMilliseconds(MmsProtocol.HttpTimeoutMs)
        };
        client.DefaultRequestHeaders.ExpectContinue = false;
        return client;
    }
}

internal readonly record struct MmsHttpResponse(
    bool Success,
    string? Body,
    MatchmakingError Error
);
