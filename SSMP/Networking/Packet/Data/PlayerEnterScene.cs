using System.Collections.Generic;
using SSMP.Math;

namespace SSMP.Networking.Packet.Data;

/// <summary>
/// Packet data for the client-bound player enter scene data.
/// </summary>
internal class ClientPlayerEnterScene : GenericClientData {
    /// <summary>
    /// The position of the player.
    /// </summary>
    public Vector2 Position { get; set; } = null!;

    /// <summary>
    /// The scale of the player.
    /// </summary>
    public bool Scale { get; set; }

    /// <summary>
    /// The ID of the animation clip of the player.
    /// </summary>
    public ushort AnimationClipId { get; set; }

    /// <summary>
    /// Construct the client player enter scene data.
    /// </summary>
    public ClientPlayerEnterScene() {
        IsReliable = true;
        DropReliableDataIfNewerExists = false;
    }

    /// <inheritdoc />
    public override void WriteData(IPacket packet) {
        packet.Write(Id);

        packet.Write(Position);
        packet.Write(Scale);

        packet.Write(AnimationClipId);
    }

    /// <inheritdoc />
    public override void ReadData(IPacket packet) {
        Id = packet.ReadUShort();

        Position = packet.ReadVector2();
        Scale = packet.ReadBool();

        AnimationClipId = packet.ReadUShort();
    }
}

/// <summary>
/// Packet data for the client player already in scene data.
/// </summary>
internal class ClientPlayerAlreadyInScene : IPacketData {
    /// <inheritdoc />
    public bool IsReliable => true;

    /// <inheritdoc />
    public bool DropReliableDataIfNewerExists => false;

    /// <summary>
    /// List of client player enter scene data instances.
    /// </summary>
    public List<ClientPlayerEnterScene> PlayerEnterSceneList { get; }

    /// <summary>
    /// List of entity spawn instances.
    /// </summary>
    public List<EntitySpawn> EntitySpawnList { get; }

    /// <summary>
    /// List of entity update instances.
    /// </summary>
    public List<EntityUpdate> EntityUpdateList { get; }
    
    /// <summary>
    /// List of entity update instances.
    /// </summary>
    public List<ReliableEntityUpdate> ReliableEntityUpdateList { get; }

    /// <summary>
    /// Whether the receiving player is scene host.
    /// </summary>
    public bool SceneHost { get; set; }

    /// <summary>
    /// The current scene host epoch.
    /// </summary>
    public uint SceneHostEpoch { get; set; }

    /// <summary>
    /// Construct the client player already in scene data.
    /// </summary>
    public ClientPlayerAlreadyInScene() {
        PlayerEnterSceneList = [];
        EntitySpawnList = [];
        EntityUpdateList = [];
        ReliableEntityUpdateList = [];
    }

    /// <inheritdoc />
    public void WriteData(IPacket packet) {
        var length = System.Math.Min(byte.MaxValue, PlayerEnterSceneList.Count);

        packet.Write((byte) length);

        for (var i = 0; i < length; i++) {
            PlayerEnterSceneList[i].WriteData(packet);
        }

        length = System.Math.Min(byte.MaxValue, EntitySpawnList.Count);

        packet.Write((byte) length);

        for (var i = 0; i < length; i++) {
            EntitySpawnList[i].WriteData(packet);
        }

        length = System.Math.Min(ushort.MaxValue, EntityUpdateList.Count);

        packet.Write((ushort) length);

        for (var i = 0; i < length; i++) {
            EntityUpdateList[i].WriteData(packet);
        }
        
        length = System.Math.Min(ushort.MaxValue, ReliableEntityUpdateList.Count);

        packet.Write((ushort) length);

        for (var i = 0; i < length; i++) {
            ReliableEntityUpdateList[i].WriteData(packet);
        }

        packet.Write(SceneHost);
        packet.Write(SceneHostEpoch);
    }

    /// <inheritdoc />
    public void ReadData(IPacket packet) {
        int length = packet.ReadByte();
        for (var i = 0; i < length; i++) {
            // Create new instance of generic type
            var instance = new ClientPlayerEnterScene();

            // Read the packet data into the instance
            instance.ReadData(packet);

            // And add it to our already initialized list
            PlayerEnterSceneList.Add(instance);
        }

        length = packet.ReadByte();
        for (var i = 0; i < length; i++) {
            // Create new instance of entity update
            var instance = new EntitySpawn();

            // Read the packet data into the instance
            instance.ReadData(packet);

            // And add it to our already initialized list
            EntitySpawnList.Add(instance);
        }

        length = packet.ReadUShort();
        for (var i = 0; i < length; i++) {
            // Get pooled instance of entity update
            var instance = ObjectPool<EntityUpdate>.Get();

            // Read the packet data into the instance
            instance.ReadData(packet);

            // And add it to our already initialized list
            EntityUpdateList.Add(instance);
        }
        
        length = packet.ReadUShort();
        for (var i = 0; i < length; i++) {
            // Get pooled instance of reliable entity update
            var instance = ObjectPool<ReliableEntityUpdate>.Get();

            // Read the packet data into the instance
            instance.ReadData(packet);

            // And add it to our already initialized list
            ReliableEntityUpdateList.Add(instance);
        }

        SceneHost = packet.ReadBool();
        SceneHostEpoch = packet.ReadUInt();
    }
}

/// <summary>
/// Packet data for the server-bound player enter scene data.
/// </summary>
internal class ServerPlayerEnterScene : IPacketData {
    /// <inheritdoc />
    public bool IsReliable => true;

    /// <inheritdoc />
    public bool DropReliableDataIfNewerExists => false;

    /// <summary>
    /// The new scene name that the player entered.
    /// </summary>
    public string NewSceneName { get; set; } = null!;

    /// <summary>
    /// The position of the player.
    /// </summary>
    public Vector2 Position { get; set; } = null!;

    /// <summary>
    /// The scale of the player.
    /// </summary>
    public bool Scale { get; set; }

    /// <summary>
    /// The ID of the animation clip of the player.
    /// </summary>
    public ushort AnimationClipId { get; set; }

    /// <inheritdoc />
    public void WriteData(IPacket packet) {
        packet.Write(NewSceneName);

        packet.Write(Position);
        packet.Write(Scale);

        packet.Write(AnimationClipId);
    }

    /// <inheritdoc />
    public void ReadData(IPacket packet) {
        NewSceneName = packet.ReadString();

        Position = packet.ReadVector2();
        Scale = packet.ReadBool();
        AnimationClipId = packet.ReadUShort();
    }
}
