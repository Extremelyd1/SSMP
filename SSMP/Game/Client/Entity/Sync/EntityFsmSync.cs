using HutongGames.PlayMaker;
using SSMP.Networking.Packet.Data;

namespace SSMP.Game.Client.Entity.Sync;

/// <summary>
/// Provides entity-specific extensions to generic FSM action synchronization.
/// </summary>
internal interface IEntityFsmSync {
    /// <summary>Appends entity-specific data to a host FSM action update.</summary>
    void AddNetworkData(EntityNetworkData data, FsmStateAction action);

    /// <summary>Applies entity-specific data after a client FSM action update.</summary>
    void ApplyNetworkData(EntityNetworkData data, FsmStateAction action);
}

/// <summary>Creates optional FSM synchronization adapters for specific entity types.</summary>
internal static class EntityFsmSyncFactory {
    /// <summary>Creates an adapter for the given entity type.</summary>
    public static IEntityFsmSync? Create(EntityType type) {
        return type switch {
            EntityType.MossMother => new MossMotherFsmSync(),
            _ => null
        };
    }
}
