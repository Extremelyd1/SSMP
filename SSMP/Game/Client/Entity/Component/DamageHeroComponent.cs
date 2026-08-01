using System.Linq;
using SSMP.Networking.Client;
using SSMP.Networking.Packet.Data;
using UnityEngine;

namespace SSMP.Game.Client.Entity.Component;

/// <inheritdoc />
/// This component manages the damage that an entity deals to the player.
internal class DamageHeroComponent : EntityComponent {
    /// <summary>
    /// Damage components on the host object, in hierarchy order.
    /// </summary>
    private readonly DamageHero[] _hostDamageHeroes;

    /// <summary>
    /// Damage components on the client clone, in hierarchy order.
    /// </summary>
    private readonly DamageHero[] _clientDamageHeroes;

    /// <summary>
    /// Last replicated damage values, in hierarchy order.
    /// </summary>
    private readonly int[] _lastDamageDealt;

    /// <summary>
    /// Last replicated active states, in hierarchy order.
    /// </summary>
    private readonly bool[] _lastActive;

    public DamageHeroComponent(
        NetClient netClient,
        ushort entityId,
        HostClientPair<GameObject> gameObject,
        DamageHero[]? hostDamageHeroes,
        DamageHero[]? clientDamageHeroes
    ) : base(netClient, entityId, gameObject) {
        _hostDamageHeroes = hostDamageHeroes ?? [];
        _clientDamageHeroes = clientDamageHeroes ?? [];
        _lastDamageDealt = _hostDamageHeroes.Select(damageHero => damageHero != null ? damageHero.damageDealt : 0)
                                            .ToArray();
        _lastActive = _hostDamageHeroes.Select(damageHero =>
            damageHero != null && damageHero.gameObject != null && damageHero.gameObject.activeSelf
        ).ToArray();
    }

    /// <summary>
    /// Callback method to check for damage hero updates.
    /// </summary>
    /// <inheritdoc />
    public override void OnUpdate() {
        if (IsControlled) {
            return;
        }

        if (GameObject.Host == null) {
            return;
        }

        var changed = false;
        for (var i = 0; i < _hostDamageHeroes.Length; i++) {
            var damageHero = _hostDamageHeroes[i];
            if (damageHero == null || damageHero.gameObject == null) {
                continue;
            }

            var damageDealt = damageHero.damageDealt;
            if (damageDealt == _lastDamageDealt[i]) {
                var active = damageHero.gameObject.activeSelf;
                if (active == _lastActive[i]) {
                    continue;
                }

                _lastActive[i] = active;
                changed = true;
                continue;
            }

            _lastDamageDealt[i] = damageDealt;
            _lastActive[i] = damageHero.gameObject.activeSelf;
            changed = true;
        }

        if (!changed) {
            return;
        }

        var data = new EntityNetworkData {
            Type = EntityComponentType.DamageHero
        };
        data.Packet.Write((byte) _hostDamageHeroes.Length);
        foreach (var damageHero in _hostDamageHeroes) {
            data.Packet.Write((byte) (damageHero != null ? damageHero.damageDealt : 0));
            data.Packet.Write(damageHero != null && damageHero.gameObject != null && damageHero.gameObject.activeSelf);
        }

        SendData(data);
    }

    /// <inheritdoc />
    protected override void InitializeHost() {
    }

    /// <inheritdoc />
    public override void Update(EntityNetworkData data, bool alreadyInSceneUpdate) {
        var length = data.Packet.ReadByte();
        for (var i = 0; i < length; i++) {
            var damageDealt = data.Packet.ReadByte();
            var active = data.Packet.ReadBool();
            if (i < _hostDamageHeroes.Length && _hostDamageHeroes[i] != null &&
                _hostDamageHeroes[i].gameObject != null) {
                _hostDamageHeroes[i].damageDealt = damageDealt;
                _hostDamageHeroes[i].gameObject.SetActive(active);
            }

            if (i < _clientDamageHeroes.Length && _clientDamageHeroes[i] != null &&
                _clientDamageHeroes[i].gameObject != null) {
                _clientDamageHeroes[i].damageDealt = damageDealt;
                _clientDamageHeroes[i].gameObject.SetActive(active);
            }
        }
    }

    /// <inheritdoc />
    public override void Destroy() {
    }
}
