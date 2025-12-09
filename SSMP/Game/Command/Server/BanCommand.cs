using System.Collections.Generic;
using System.Linq;
using System.Net;
using SSMP.Api.Command.Server;
using SSMP.Game.Server;
using SSMP.Game.Server.Auth;
using SSMP.Networking.Packet.Data;
using SSMP.Util;
using SSMP.Api.Command;

namespace SSMP.Game.Command.Server;

/// <summary>
/// Command for banning users by username, auth key, Steam ID, or IP address.
/// Supports both regular bans and IP bans, as well as unbanning.
/// </summary>
internal class BanCommand : IServerCommand, ICommandWithDescription {
    /// <inheritdoc />
    public string Trigger => "/ban";

    /// <inheritdoc />
    public string[] Aliases => ["/unban", "/banip", "/unbanip"];

    /// <inheritdoc />
    public string Description =>
        "Ban players by auth key or username. IP bans will ban the player's IP address (UDP clients) or Steam ID (Steam clients).";

    /// <inheritdoc />
    public bool AuthorizedOnly => true;

    /// <summary>
    /// </summary>
    private readonly BanList _banList;

    /// <summary>
    /// The server manager instance.
    /// </summary>
    private readonly ServerManager _serverManager;

    /// <summary>
    /// Constructs a new ban command with the given dependencies.
    /// </summary>
    /// <param name="banList">The ban list instance.</param>
    /// <param name="serverManager">The server manager instance.</param>
    public BanCommand(BanList banList, ServerManager serverManager) {
        _banList = banList;
        _serverManager = serverManager;
    }

    /// <inheritdoc />
    public void Execute(ICommandSender commandSender, string[] args) {
        var commandType = ParseCommandType(args[0]);
        
        if (args.Length < 2) {
            SendUsage(commandSender, commandType);
            return;
        }

        var identifier = args[1];

        // Handle "all" keyword for clearing bans
        if (identifier == "all" && commandType.IsUnban) {
            HandleClearAllBans(commandSender, commandType.IsIpBan);
            return;
        }

        if (commandType.IsUnban) {
            HandleUnban(commandSender, identifier, commandType.IsIpBan);
        } else {
            HandleBan(commandSender, identifier, commandType.IsIpBan);
        }
    }

    /// <summary>
    /// Parses the command type from the trigger string.
    /// </summary>
    private static CommandType ParseCommandType(string trigger) => new(
        IsIpBan: trigger.Contains("ip"),
        IsUnban: trigger.Contains("unban")
    );

    /// <summary>
    /// Handles unbanning operations for auth keys or IP addresses.
    /// </summary>
    private void HandleUnban(ICommandSender sender, string identifier, bool isIpBan) {
        if (isIpBan) {
            // UnbanIP Logic
            // Try to resolve as Username first to get identifier
            if (CommandUtil.TryGetPlayerByName(_serverManager.Players, identifier, out var player)) {
                var playerData = (ServerPlayerData)player;
                UnbanIdentifier(sender, playerData.UniqueClientIdentifier);
                return;
            }

            // Try to resolve as AuthKey to get identifier (User mentioned reverse search)
             if (AuthUtil.IsValidAuthKey(identifier)) {
                 if (CommandUtil.TryGetPlayerByAuthKey(_serverManager.Players.Cast<ServerPlayerData>(), identifier, out var authKeyPlayer)) {
                     UnbanIdentifier(sender, authKeyPlayer.UniqueClientIdentifier);
                     return;
                 }
            }

            // Assume Identifier (IP or SteamID)
            UnbanIdentifier(sender, identifier);

        } else {
            // Unban logic (AuthKey based)

            // Try to resolve as Username first to get AuthKey
            if (CommandUtil.TryGetPlayerByName(_serverManager.Players, identifier, out var player)) {
                 var playerData = (ServerPlayerData)player;
                 UnbanAuthKey(sender, playerData.AuthKey);
                 return;
            }
            
            // Assume AuthKey
            if (AuthUtil.IsValidAuthKey(identifier)) {
                UnbanAuthKey(sender, identifier);
                return;
            }

            sender.SendMessage($"Could not find player or valid AuthKey matching '{identifier}'");
        }
    }

    /// <summary>
    /// Handles banning operations by identifier.
    /// </summary>
    private void HandleBan(ICommandSender sender, string identifier, bool isIpBan) {
        var players = _serverManager.Players.Cast<ServerPlayerData>().ToList();

        if (isIpBan) {
            // /banip logic: Target Identifier (IP/SteamID)
            
            // 1. Try IP Address directly
            if (IPAddress.TryParse(identifier, out var address)) {
                BanIdentifier(sender, address.ToString(), players);
                return;
            }

            // 2. Try Username -> Identifier resolution
            if (CommandUtil.TryGetPlayerByName(_serverManager.Players, identifier, out var player)) {
                var playerData = (ServerPlayerData)player;
                BanIdentifier(sender, playerData.UniqueClientIdentifier, players);
                return;
            }

            // 3. Try AuthKey -> Identifier resolution
            if (AuthUtil.IsValidAuthKey(identifier)) {
                 if (CommandUtil.TryGetPlayerByAuthKey(players, identifier, out var authPlayer)) {
                     BanIdentifier(sender, authPlayer.UniqueClientIdentifier, players);
                     return;
                 }
            }

            // 4. Fallback: Assume the input IS the Identifier (e.g. SteamID)
            BanIdentifier(sender, identifier, players);

        } else {
            // /ban logic: Target AuthKey

             // 1. Try Username -> AuthKey resolution
            if (CommandUtil.TryGetPlayerByName(_serverManager.Players, identifier, out var player)) {
                var playerData = (ServerPlayerData)player;
                BanAuthKey(sender, playerData);
                return;
            }

            // 2. Try direct AuthKey
            if (AuthUtil.IsValidAuthKey(identifier)) {
                // We create a dummy/temporary wrapper or just handle the key directly.
                 CommandUtil.TryGetPlayerByAuthKey(players, identifier, out var existingPlayer);
                 if (existingPlayer != null) {
                      BanAuthKey(sender, existingPlayer);
                 } else {
                      // Offline ban by key
                      _banList.Add(identifier);
                      sender.SendMessage($"Auth key '{identifier}' has been banned.");
                 }
                 return;
            }
             
            sender.SendMessage($"Could not find player or valid AuthKey matching '{identifier}'");
        }
    }


    private void BanIdentifier(ICommandSender sender, string identifier, List<ServerPlayerData> players) {
        if (!_banList.AddIp(identifier)) {
            sender.SendMessage($"Identifier '{identifier}' is already banned.");
            return;
        }
        
        // Try to find player online with this identifier to kick
        // Check for exact match (SteamID) or IP match
        var isIp = IPAddress.TryParse(identifier, out _);
        var msg = isIp ? "IP Address" : "Identifier";
        sender.SendMessage($"{msg} '{identifier}' has been banned");

        foreach(var p in players) {
             // For UDP, UniqueClientIdentifier is IP:Port. Identifier is IP.
             // For Steam, both are SteamID.
             bool match = false;
             if (isIp) {
                 // For UDP, UniqueClientIdentifier is "IP:Port", so extract just the IP
                 var playerIp = p.UniqueClientIdentifier.Split(':')[0];
                 if (playerIp == identifier) match = true;
             } else {
                  if (p.UniqueClientIdentifier == identifier) match = true;
             }
             
             if (match) {
                 DisconnectPlayer(p);
             }
        }
    }

    private void BanAuthKey(ICommandSender sender, ServerPlayerData playerData) {
         if (!_banList.Add(playerData.AuthKey)) {
             sender.SendMessage($"Player '{playerData.Username}' is already banned (AuthKey).");
             // Disconnect anyway
             DisconnectPlayer(playerData);
             return;
         }
         
         sender.SendMessage($"Player '{playerData.Username}' has been banned (AuthKey).");
         DisconnectPlayer(playerData);
    }

    private void UnbanIdentifier(ICommandSender sender, string identifier) {
         if (!_banList.RemoveIp(identifier)) {
             sender.SendMessage($"Identifier '{identifier}' is not banned.");
             return;
         }
         sender.SendMessage($"Identifier '{identifier}' has been unbanned.");
    }

    private void UnbanAuthKey(ICommandSender sender, string authKey) {
        if (!_banList.Remove(authKey)) {
            sender.SendMessage($"Auth key '{authKey}' is not banned.");
            return;
        }
        sender.SendMessage($"Auth key '{authKey}' has been unbanned.");
    }
    
    /// <summary>
    /// Clears all bans of a specific type.
    /// </summary>
    private void HandleClearAllBans(ICommandSender sender, bool isIpBan) {
        if (isIpBan) {
            _banList.ClearIps();
            sender.SendMessage("Cleared all IP addresses from ban list");
        } else {
            _banList.Clear();
            sender.SendMessage("Cleared all auth keys from ban list");
        }
    }

    /// <summary>
    /// Disconnects a player with a banned status.
    /// </summary>
    private void DisconnectPlayer(ServerPlayerData playerData) => 
        _serverManager.InternalDisconnectPlayer(playerData.Id, DisconnectReason.Banned);

    /// <summary>
    /// Sends appropriate usage information based on command type.
    /// </summary>
    private void SendUsage(ICommandSender sender, CommandType type) {
        var message = (type.IsIpBan, type.IsUnban) switch {
            (true, true) => $"{Aliases[3]} <username|auth key|ip|steam id|all>",
            (true, false) => $"{Aliases[1]} <username|auth key|ip|steam id>",
            (false, true) => $"{Aliases[0]} <username|auth key|all>",
            (false, false) => $"{Trigger} <username|auth key>"
        };

        sender.SendMessage(message);
    }
    /// <summary>
    /// Represents the type of ban command being executed.
    /// </summary>
    private readonly record struct CommandType(bool IsIpBan, bool IsUnban);
}