using System;
using System.Reflection;
using MonoMod.RuntimeDetour;
using SSMP.Networking.Client;
using SSMP.Networking.Packet.Data;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Game.Client.Entity.Component;

// TODO: make sure that the data sent on death is saved as state on the server, so new clients entering
// scenes can start with the entity disabled/already dead
// TODO: periodically (or on hit) sync the health of the entity so on scene host transfer we can reset health
/// <inheritdoc />
/// <summary>
/// Manages the <see cref="HealthManager"/> component of the entity, polling host-side state
/// each frame and broadcasting changes to connected clients.
/// </summary>
internal class HealthManagerComponent : EntityComponent {
    private const BindingFlags HookBindingFlags =
        BindingFlags.Public |
        BindingFlags.NonPublic |
        BindingFlags.Instance;

    private static readonly Type[] DieHookParameterTypes = [typeof(float?), typeof(AttackTypes), typeof(bool)];

    private readonly HostClientPair<HealthManager> _healthManager;
    private readonly Hook _dieHook;

    // Guards against OnUpdate running after Destroy() within the same frame,
    // and makes double-Destroy() safe.
    private bool _isDestroyed;
    private bool _lastInvincible;
    private int _lastInvincibleFromDirection;
    private int _lastHp;

    /// <summary>
    /// Set before calling the three-argument Die overload on the client instance so the
    /// hook can distinguish a network-triggered death from a local one.
    /// </summary>
    private bool _allowDeath;

    public HealthManagerComponent(
        NetClient netClient,
        ushort entityId,
        HostClientPair<GameObject> gameObject,
        HostClientPair<HealthManager> healthManager
    ) : base(netClient, entityId, gameObject) {
        _healthManager = healthManager;

        // Seed last-known values so the first OnUpdate() doesn't spuriously broadcast.
        // Host is expected to be non-null here; if it is null, we default to zero-values
        // and will send on the first frame once Host becomes valid.
        var host = healthManager.Host;
        if (host != null) {
            _lastInvincible = host.IsInvincible;
            _lastInvincibleFromDirection = host.InvincibleFromDirection;
            _lastHp = host.hp;
        }

        var dieMethod = Array.Find(
            typeof(HealthManager).GetMethods(HookBindingFlags),
            method => method.Name == nameof(HealthManager.Die)
                      && HasParameterTypes(method, DieHookParameterTypes)
        );
        if (dieMethod == null) {
            throw new MissingMethodException(
                typeof(HealthManager).FullName,
                $"{nameof(HealthManager.Die)}(float?, AttackTypes, bool)"
            );
        }

        _dieHook = new Hook(
            dieMethod,
            (Action<Action<HealthManager, float?, AttackTypes, bool>, HealthManager, float?, AttackTypes, bool>)
            HealthManagerOnDie
        );

        MonoBehaviourUtil.Instance.OnUpdateEvent += OnUpdate;
    }

    /// <summary>
    /// Callback method for when the health manager dies.
    /// </summary>
    private void HealthManagerOnDie(
        Action<HealthManager, float?, AttackTypes, bool> orig,
        HealthManager self,
        float? attackDirection,
        AttackTypes attackType,
        bool ignoreEvasion
    ) {
        if (self != _healthManager.Host && self != _healthManager.Client) {
            orig(self, attackDirection, attackType, ignoreEvasion);
            return;
        }

        if (self == _healthManager.Client) {
            if (!_allowDeath) {
                Logger.Info("HealthManager Die was called on client entity");
            } else {
                Logger.Info("HealthManager Die was called on client entity, but it is allowed death");
                orig(self, attackDirection, attackType, ignoreEvasion);
                _allowDeath = false;
            }

            return;
        }

        Logger.Info("HealthManager Die was called on host entity");
        orig(self, attackDirection, attackType, ignoreEvasion);

        if (IsControlled) {
            return;
        }

        var data = new EntityNetworkData { Type = EntityComponentType.Death };
        if (attackDirection.HasValue) {
            data.Packet.Write(true);
            data.Packet.Write(attackDirection.Value);
        } else {
            data.Packet.Write(false);
        }

        data.Packet.Write((byte) attackType);
        data.Packet.Write(ignoreEvasion);
        SendData(data);
    }

    /// <summary>
    /// Polls the host <see cref="HealthManager"/> for state changes. Allocates and sends a
    /// network packet only when at least one tracked value has changed; no allocation occurs
    /// on unchanged frames.
    /// </summary>
    private void OnUpdate() {
        if (_isDestroyed) return;
        if (IsControlled) return;

        var host = _healthManager.Host;
        if (host == null) return;

        var newInvincible = host.IsInvincible;
        var newInvincibleFromDir = host.InvincibleFromDirection;
        var newHp = host.hp;

        // Early return before any allocation. This is the common case: state rarely changes.
        if (newInvincible == _lastInvincible
            && newInvincibleFromDir == _lastInvincibleFromDirection
            && newHp == _lastHp) {
            return;
        }

        // Update all three together: the packet always carries the full current state, so
        // tracking them independently would not reduce packet count.
        _lastInvincible = newInvincible;
        _lastInvincibleFromDirection = newInvincibleFromDir;
        _lastHp = newHp;

        var data = new EntityNetworkData { Type = EntityComponentType.Invincibility };
        data.Packet.Write(newInvincible);
        data.Packet.Write((byte) newInvincibleFromDir);
        data.Packet.Write(newHp);
        SendData(data);
    }

    /// <inheritdoc />
    public override void InitializeHost() {
    }

    /// <inheritdoc />
    public override void Update(EntityNetworkData data, bool alreadyInSceneUpdate) {
        Logger.Info("Received health manager update");

        if (!IsControlled) {
            Logger.Info("  Entity was not controlled");
            return;
        }

        if (data.Type == EntityComponentType.Death) {
            float? attackDirection = null;
            if (data.Packet.ReadBool()) {
                attackDirection = data.Packet.ReadFloat();
            }

            var attackType = (AttackTypes) data.Packet.ReadByte();
            var ignoreEvasion = data.Packet.ReadBool();

            // Gate the Die hook to permit only network-triggered deaths on the client instance.
            _allowDeath = true;
            _healthManager.Client.Die(attackDirection, attackType, ignoreEvasion);
        } else if (data.Type == EntityComponentType.Invincibility) {
            var newInvincible = data.Packet.ReadBool();
            var newInvincibleFromDir = data.Packet.ReadByte();
            var newHp = data.Packet.ReadInt();

            if (_healthManager.Host != null) {
                _healthManager.Host.IsInvincible = newInvincible;
                _healthManager.Host.InvincibleFromDirection = newInvincibleFromDir;
                _healthManager.Host.hp = newHp;
            }

            if (_healthManager.Client == null) {
                return;
            }

            _healthManager.Client.IsInvincible = newInvincible;
            _healthManager.Client.InvincibleFromDirection = newInvincibleFromDir;
            _healthManager.Client.hp = newHp;
        }
    }

    /// <inheritdoc />
    public override void Destroy() {
        if (_isDestroyed) return;
        _isDestroyed = true;

        _dieHook.Dispose();
        if (MonoBehaviourUtil.Instance != null) {
            MonoBehaviourUtil.Instance.OnUpdateEvent -= OnUpdate;
        }
    }

    private static bool HasParameterTypes(MethodInfo method, Type[] parameterTypes) {
        var parameters = method.GetParameters();
        if (parameters.Length != parameterTypes.Length) {
            return false;
        }

        for (var i = 0; i < parameters.Length; i++) {
            if (parameters[i].ParameterType != parameterTypes[i]) {
                return false;
            }
        }

        return true;
    }
}
