using System.Collections.Concurrent;
using System.Reflection;
using System.Resources;
using System.Runtime.Serialization;
using System.Text.Json;

namespace MMS.Services;

/// <summary>
/// Lobby name providing service that randomly generates lobby names from words in an embedded JSON.
/// </summary>
public class LobbyNameService {
    /// <summary>
    /// The file path to the lobby name data as embedded resource.
    /// </summary>
    private const string LobbyNameDataFilePath = "MMS.Resources.lobby-name-data.json";

    /// <summary>
    /// Lobby name data that is loaded in the constructor.
    /// </summary>
    private readonly LobbyNameData _lobbyNameData;

    /// <summary>
    /// Effectively a hash set containing lobby names that are in use and should not be re-used again.
    /// ConcurrentDictionary is used to allow concurrency, since there is no concurrent hash set alternative.
    /// The byte value is always 0.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _usedLobbyNames = [];

    /// <summary>
    /// Random instance to use for generating lobby names.
    /// </summary>
    private readonly Random _random = new();

    public LobbyNameService() {
        var resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(LobbyNameDataFilePath);
        if (resourceStream == null) {
            throw new MissingManifestResourceException("Could not load lobby name data from embedded resource");
        }

        using var streamReader = new StreamReader(resourceStream);
        var fileString = streamReader.ReadToEnd();

        var data = JsonSerializer.Deserialize<LobbyNameData>(fileString);
        if (data == null) {
            throw new SerializationException("Could not deserialize lobby name data from embedded resource");
        }

        _lobbyNameData = data;
    }

    /// <summary>
    /// Generate a lobby name.
    /// </summary>
    /// <returns>The lobby name as a string.</returns>
    public string GenerateLobbyName() {
        const int maxTries = 100;
        
        string lobbyName;
        // Keep track of the number of tries of randomly generating lobby name
        var tryCount = 1;

        do {
            var adjective = _lobbyNameData.Adjectives[_random.Next(_lobbyNameData.Adjectives.Count)];
            var noun = _lobbyNameData.Nouns[_random.Next(_lobbyNameData.Nouns.Count)];
            var verb = _lobbyNameData.Verbs[_random.Next(_lobbyNameData.Verbs.Count)];
            var adverb = _lobbyNameData.Adverbs[_random.Next(_lobbyNameData.Adverbs.Count)];

            lobbyName = adjective + noun + verb + adverb;
        } while (_usedLobbyNames.ContainsKey(lobbyName) && tryCount++ < maxTries);
        
        if (tryCount > maxTries) {
            // tryCount has increased past maxTries and failed the check in the while loop, so we exited the while
            // loop due to running out of attempts. Fall back to a generic lobby name
            lobbyName = "Generic Lobby";
        } else {
            _usedLobbyNames.TryAdd(lobbyName, 0);
        }

        return lobbyName;
    }

    /// <summary>
    /// Free up the given lobby name from the used names.
    /// </summary>
    /// <param name="lobbyName">The used lobby name to free up.</param>
    public void FreeLobbyName(string lobbyName) {
        _usedLobbyNames.TryRemove(lobbyName, out _);
    }

    /// <summary>
    /// Deserializable class with lobby name data for the various words that are used to generate lobby names.
    /// </summary>
    private class LobbyNameData {
        /// <summary>
        /// List of adjectives for lobby name generation.
        /// </summary>
        public required List<string> Adjectives { get; init; }
        /// <summary>
        /// List of nouns for lobby name generation.
        /// </summary>
        public required List<string> Nouns { get; init; }
        /// <summary>
        /// List of verbs for lobby name generation.
        /// </summary>
        public required List<string> Verbs { get; init; }
        /// <summary>
        /// List of adverbs for lobby name generation.
        /// </summary>
        public required List<string> Adverbs { get; init; }
    }
}
