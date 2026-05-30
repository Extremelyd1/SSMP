using System;
using System.Collections.Generic;
using SSMP.Api.Addon;
using SSMP.Game;
using SSMP.Internals;

namespace SSMP.Networking.Packet.Data;

/// <summary>
/// Packet data for the server info data.
/// </summary>
internal class ServerInfo : IPacketData {
    /// <inheritdoc />
    public bool IsReliable => false;

    /// <inheritdoc />
    public bool DropReliableDataIfNewerExists => false;

    /// <summary>
    /// The result of the connection, whether it was accepted.
    /// </summary>
    public ServerConnectionResult ConnectionResult { get; set; }

    /// <summary>
    /// The message detailing why the connection was rejected if it was.
    /// </summary>
    public string ConnectionRejectedMessage { get; set; } = null!;

    /// <summary>
    /// List of addon data that the server uses.
    /// </summary>
    public List<AddonData> AddonData { get; set; } = [];

    /// <summary>
    /// The order in which the addons have been assigned IDs.
    /// </summary>
    public byte[] AddonOrder { get; set; } = null!;

    /// <summary>
    /// The server settings for the server. Packaged as a <see cref="Data.ServerSettingsUpdate"/> to allow serialization
    /// to packet data.
    /// </summary>
    public ServerSettingsUpdate ServerSettingsUpdate { get; set; } = null!;

    /// <summary>
    /// Whether full synchronisation is enabled for the server.
    /// </summary>
    public bool FullSynchronisation { get; set; }

    /// <summary>
    /// The save data currently used on the server.
    /// </summary>
    public CurrentSave CurrentSave { get; set; } = null!;

    /// <summary>
    /// List of ID, username pairs for each connected client.
    /// </summary>
    public List<PlayerInfo> PlayerInfos { get; set; } = null!;

    /// <inheritdoc />
    public void WriteData(IPacket packet) {
        packet.Write((byte) ConnectionResult);

        if (ConnectionResult == ServerConnectionResult.Accepted) {
            packet.Write((byte) AddonOrder.Length);

            foreach (var addonOrderByte in AddonOrder) {
                packet.Write(addonOrderByte);
            }

            ServerSettingsUpdate.WriteData(packet);
            
            packet.Write(FullSynchronisation);

            // CurrentSave.WriteData(packet);
        
            packet.Write((ushort) PlayerInfos.Count);

            foreach (var playerInfo in PlayerInfos) {
                playerInfo.WriteData(packet);
            }

            return;
        }

        if (ConnectionResult == ServerConnectionResult.InvalidAddons) {
            var addonDataLength = (byte) System.Math.Min(byte.MaxValue, AddonData.Count);

            packet.Write(addonDataLength);

            for (var i = 0; i < addonDataLength; i++) {
                packet.Write(AddonData[i].Identifier);
                packet.Write(AddonData[i].Version);
            }

            return;
        }

        packet.Write(ConnectionRejectedMessage);
    }

    /// <inheritdoc />
    public void ReadData(IPacket packet) {
        ConnectionResult = (ServerConnectionResult) packet.ReadByte();

        if (ConnectionResult == ServerConnectionResult.Accepted) {
            var addonOrderLength = packet.ReadByte();
            AddonOrder = new byte[addonOrderLength];

            for (var i = 0; i < addonOrderLength; i++) {
                AddonOrder[i] = packet.ReadByte();
            }

            ServerSettingsUpdate = new ServerSettingsUpdate();
            ServerSettingsUpdate.ReadData(packet);

            FullSynchronisation = packet.ReadBool();

            // CurrentSave = new CurrentSave();
            // CurrentSave.ReadData(packet);
        
            var length = packet.ReadUShort();

            PlayerInfos = [];
            for (var i = 0; i < length; i++) {
                PlayerInfos.Add(PlayerInfo.ReadData(packet));
            }

            return;
        }

        if (ConnectionResult == ServerConnectionResult.InvalidAddons) {
            var addonDataLength = packet.ReadByte();

            AddonData = new List<AddonData>();

            for (var i = 0; i < addonDataLength; i++) {
                var id = packet.ReadString();
                var version = packet.ReadString();

                if (id.Length > Addon.MaxNameLength || version.Length > Addon.MaxVersionLength) {
                    throw new ArgumentException("Identifier or version of addon exceeds max length");
                }

                AddonData.Add(new AddonData(id, version));
            }

            return;
        }

        ConnectionRejectedMessage = packet.ReadString();
    }

    /// <summary>
    /// Class for player info that is used in the server info sent to the player.
    /// </summary>
    public class PlayerInfo {
        /// <summary>
        /// The ID of the player.
        /// </summary>
        public required ushort Id { get; init; }
        /// <summary>
        /// The username of the player.
        /// </summary>
        public required string Username { get; init; }
        /// <summary>
        /// The team of the player.
        /// </summary>
        public required Team Team { get; init; }
        /// <summary>
        /// The skin ID of the player.
        /// </summary>
        public required byte SkinId { get; init; }
        /// <summary>
        /// The current crest type of the player.
        /// </summary>
        public required CrestType CrestType { get; init; }

        /// <inheritdoc cref="IPacketData.WriteData" />
        public void WriteData(IPacket packet) {
            packet.Write(Id);
            packet.Write(Username);
            packet.Write((byte) Team);
            packet.Write(SkinId);
            packet.Write((byte) CrestType);
        }

        /// <summary>
        /// Read the data from the given packet into a new instance of <see cref="PlayerInfo"/>.
        /// </summary>
        /// <param name="packet">The packet to read from.</param>
        /// <returns>A new instance of <see cref="PlayerInfo"/>.</returns>
        public static PlayerInfo ReadData(IPacket packet) {
            return new PlayerInfo {
                Id = packet.ReadUShort(),
                Username = packet.ReadString(),
                Team = (Team) packet.ReadByte(),
                SkinId = packet.ReadByte(),
                CrestType = (CrestType) packet.ReadByte()
            };
        }
    }
}
