using SSMP.Networking.Client;
using SSMP.Networking.Packet.Data;
using UnityEngine;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Game.Client.Entity.Component;

/// <inheritdoc />
/// This component manages the <see cref="Collider2D"/> unity component of an entity.
internal class ColliderComponent : EntityComponent {
    /// <summary>
    /// Host-client pair for the box collider of the entity.
    /// </summary>
    private readonly HostClientPair<Collider2D> _collider;

    /// <summary>
    /// Optional bool indicating whether the collider was last enabled.
    /// </summary>
    private bool? _lastEnabled;

    public ColliderComponent(
        NetClient netClient,
        ushort entityId,
        HostClientPair<GameObject> gameObject,
        HostClientPair<Collider2D> collider
    ) : base(netClient, entityId, gameObject) {
        _collider = collider;
    }

    /// <summary>
    /// Callback for checking the collider each update.
    /// </summary>
    /// <inheritdoc />
    public override void OnUpdate() {
        if (IsControlled) {
            return;
        }

        if (_collider.Host == null) {
            return;
        }

        var newEnabled = _collider.Host.enabled;
        if (!_lastEnabled.HasValue || newEnabled != _lastEnabled.Value) {
            var hostName = GameObject?.Host != null ? GameObject.Host.name : "Unknown";
            Logger.Info($"Collider of {hostName} enabled changed to: {newEnabled}");
            _lastEnabled = newEnabled;

            var data = new EntityNetworkData {
                Type = EntityComponentType.Collider
            };
            data.Packet.Write(newEnabled);

            SendData(data);
        }
    }

    /// <inheritdoc />
    protected override void InitializeHost() {
    }

    /// <inheritdoc />
    public override void Update(EntityNetworkData data, bool alreadyInSceneUpdate) {
        var clientName = GameObject?.Client != null ? GameObject.Client.name : "Unknown";
        Logger.Info($"Received collider update for {clientName}");

        if (!IsControlled) {
            Logger.Info("  Entity was not controlled");
            return;
        }

        if (data.Packet.Length < 1) {
            Logger.Warn($"Received empty collider update for entity {clientName}, ignoring");
            return;
        }

        var enabled = data.Packet.ReadBool();
        if (_collider.Host != null) {
            _collider.Host.enabled = enabled;
        }

        if (_collider.Client != null) {
            _collider.Client.enabled = enabled;
        }

        Logger.Info($"  Enabled: {enabled}");
    }

    /// <inheritdoc />
    public override void Destroy() {
    }
}
