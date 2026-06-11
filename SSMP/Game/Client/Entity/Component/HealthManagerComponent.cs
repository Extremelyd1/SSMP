using System;
using System.Reflection;
using MonoMod.RuntimeDetour;
using SSMP.Networking.Client;
using SSMP.Networking.Packet.Data;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;

#pragma warning disable CS0414 // Field is assigned but its value is never used

namespace SSMP.Game.Client.Entity.Component;

// TODO: preserve entity HP and AI/FSM state during scene host transfer.
/// <inheritdoc />
/// This component manages the <see cref="HealthManager"/> component of the entity.
internal class HealthManagerComponent : EntityComponent {
    private const BindingFlags HookBindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>
    /// Host-client pair of health manager components of the entity.
    /// </summary>
    private readonly HostClientPair<HealthManager> _healthManager;

    /// <summary>
    /// Boolean indicating whether the health manager of the client entity is allowed to die.
    /// </summary>
    private bool _allowDeath;

    /// <summary>
    /// MonoMod hook for HealthManager.Die.
    /// </summary>
    private Hook? _healthManagerDieHook;

    /// <summary>
    /// The last value for the "invincible" variable of the health manager.
    /// </summary>
    private bool _lastInvincible;

    /// <summary>
    /// The last synced HP value of the health manager.
    /// </summary>
    private int _lastHp;

    /// <summary>
    /// The starting HP of the health manager.
    /// Used to clamp incoming healing from other clients.
    /// </summary>
    private readonly int _initialHp;

    /// <summary>
    /// The last value for the "invincibleFromDirection" variable of the health manager.
    /// </summary>
    private int _lastInvincibleFromDirection;

    /// <summary>
    /// The current scene host epoch from our perspective.
    /// </summary>
    private uint _currentHealthEpoch;

    /// <summary>
    /// The highest received scene host epoch.
    /// </summary>
    private uint _lastReceivedHealthEpoch;

    /// <summary>
    /// Monotonically increasing local health update ID sent to other clients.
    /// </summary>
    private uint _nextHealthUpdateId;

    /// <summary>
    /// Dictionary of the last received health update ID per sender to prevent duplicate/out-of-order updates.
    /// </summary>
    private readonly System.Collections.Generic.Dictionary<ushort, uint> _lastReceivedHealthUpdateIdBySender = new();

    public HealthManagerComponent(
        NetClient netClient,
        ushort entityId,
        HostClientPair<GameObject> gameObject,
        HostClientPair<HealthManager> healthManager
    ) : base(netClient, entityId, gameObject) {
        _healthManager = healthManager;

        _lastInvincible = healthManager.Host.IsInvincible;
        _lastHp = healthManager.Host.hp;
        _initialHp = healthManager.Host.hp;
        _lastInvincibleFromDirection = healthManager.Host.InvincibleFromDirection;

        var dieMethod = Array.Find(
            typeof(HealthManager).GetMethods(HookBindingFlags),
            method =>
                method.Name == nameof(HealthManager.Die) &&
                method.GetParameters() is { Length: 3 } parameters &&
                parameters[0].ParameterType == typeof(float?) &&
                parameters[1].ParameterType == typeof(AttackTypes) &&
                parameters[2].ParameterType == typeof(bool)
        );

        if (dieMethod == null) {
            throw new MissingMethodException(
                typeof(HealthManager).FullName,
                $"{nameof(HealthManager.Die)}(float?, {nameof(AttackTypes)}, bool)"
            );
        }

        _healthManagerDieHook = new Hook(dieMethod, HealthManagerOnDie);
        MonoBehaviourUtil.Instance.OnUpdateEvent += OnUpdate;
    }

    /// <summary>
    /// Callback method for when the health manager dies.
    /// </summary>
    /// <param name="orig">The original method.</param>
    /// <param name="self">The health manager instance.</param>
    /// <param name="attackDirection">The direction of the attack that caused the death.</param>
    /// <param name="attackType">The type of attack that caused the death.</param>
    /// <param name="ignoreEvasion">Whether to ignore evasion.</param>
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

        var data = new EntityNetworkData {
            Type = EntityComponentType.Death
        };

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
    /// Callback method for updates to check whether health or invincibility changes.
    /// </summary>
    private void OnUpdate() {
        var observedHealthManager = IsControlled ? _healthManager.Client : _healthManager.Host;
        if (observedHealthManager == null) {
            return;
        }

        var newHp = observedHealthManager.hp;
        if (newHp != _lastHp) {
            var previousHp = _lastHp;
            _lastHp = newHp;
            _nextHealthUpdateId++;

            var hpData = new EntityNetworkData {
                Type = EntityComponentType.Health
            };

            hpData.Packet.Write(previousHp);
            hpData.Packet.Write(newHp);
            hpData.Packet.Write(_currentHealthEpoch);
            hpData.Packet.Write(_nextHealthUpdateId);

            SendData(hpData);
        }

        var invincibilityData = new EntityNetworkData {
            Type = EntityComponentType.Invincibility
        };

        var shouldSendInvincibility = false;

        var newInvincible = _healthManager.Host.IsInvincible;
        if (newInvincible != _lastInvincible) {
            _lastInvincible = newInvincible;
            shouldSendInvincibility = true;
        }

        invincibilityData.Packet.Write(newInvincible);

        var newInvincibleFromDir = _healthManager.Host.InvincibleFromDirection;
        if (newInvincibleFromDir != _lastInvincibleFromDirection) {
            _lastInvincibleFromDirection = newInvincibleFromDir;
            shouldSendInvincibility = true;
        }

        invincibilityData.Packet.Write((byte) newInvincibleFromDir);

        if (shouldSendInvincibility) {
            SendData(invincibilityData);
        }
    }

    /// <inheritdoc />
    public override void InitializeHost(uint sceneHostEpoch) {
        ResetHealthOrderingForEpoch(sceneHostEpoch);
        var currentHp = GetCurrentHp();
        ApplyHp(currentHp, triggerHostDeath: false);
    }

    /// <inheritdoc />
    public override void InitializeClient(uint sceneHostEpoch) {
        ResetHealthOrderingForEpoch(sceneHostEpoch);
        var currentHp = GetCurrentHp();
        ApplyHp(currentHp, triggerHostDeath: false);
    }

    private void ResetHealthOrderingForEpoch(uint sceneHostEpoch) {
        _currentHealthEpoch = sceneHostEpoch;
        _lastReceivedHealthEpoch = sceneHostEpoch;
        _nextHealthUpdateId = 0;
        _lastReceivedHealthUpdateIdBySender.Clear();
    }

    /// <inheritdoc />
    public override void Update(EntityNetworkData data, bool alreadyInSceneUpdate) {
        Logger.Info("Received health manager update");

        if (!IsControlled && data.Type != EntityComponentType.Health) {
            Logger.Info("  Entity was not controlled");
            return;
        }

        if (data.Type == EntityComponentType.Death) {
            var attackDirection = new float?();
            if (data.Packet.ReadBool()) {
                attackDirection = data.Packet.ReadFloat();
            }

            var attackType = (AttackTypes) data.Packet.ReadByte();
            var ignoreEvasion = data.Packet.ReadBool();

            // Set a boolean to indicate that the client health manager is allowed to execute the Die method
            _allowDeath = true;
            _healthManager.Client.Die(attackDirection, attackType, ignoreEvasion);
        } else if (data.Type == EntityComponentType.Health) {
            UpdateHealth(data, alreadyInSceneUpdate);
        } else if (data.Type == EntityComponentType.Invincibility) {
            var newInvincible = data.Packet.ReadBool();
            var newInvincibleFromDir = data.Packet.ReadByte();

            if (_healthManager.Host != null) {
                _healthManager.Host.IsInvincible = newInvincible;
                _healthManager.Host.InvincibleFromDirection = newInvincibleFromDir;
            }

            if (_healthManager.Client == null) {
                return;
            }

            _healthManager.Client.IsInvincible = newInvincible;
            _healthManager.Client.InvincibleFromDirection = newInvincibleFromDir;
        }
    }

    /// <summary>
    /// Applies a health update from the network.
    /// Scene snapshots are applied as absolute HP,
    /// while live updates are merged as HP deltas
    /// so delayed packets do not overwrite local damage or healing.
    /// </summary>
    private void UpdateHealth(EntityNetworkData data, bool alreadyInSceneUpdate) {
        var previousHp = data.Packet.ReadInt();
        var newHp = data.Packet.ReadInt();
        var healthEpoch = data.Packet.ReadUInt();
        var healthUpdateId = data.Packet.ReadUInt();

        if (alreadyInSceneUpdate) {
            ResetHealthOrderingForEpoch(healthEpoch);
            ApplyHp(newHp, triggerHostDeath: false);
        } else {
            if (healthEpoch < _lastReceivedHealthEpoch) {
                return;
            }

            if (healthEpoch > _lastReceivedHealthEpoch) {
                _lastReceivedHealthEpoch = healthEpoch;
                _lastReceivedHealthUpdateIdBySender.Clear();
                if (IsControlled) {
                    _currentHealthEpoch = healthEpoch;
                }
            }

            var senderId = data.SenderId;
            if (_lastReceivedHealthUpdateIdBySender.TryGetValue(senderId, out var lastId) && healthUpdateId <= lastId) {
                return;
            }

            _lastReceivedHealthUpdateIdBySender[senderId] = healthUpdateId;

            var currentHp = GetCurrentHp();
            var damage = System.Math.Max(previousHp - newHp, 0);
            var healing = System.Math.Max(newHp - previousHp, 0);

            var targetHp = currentHp;
            if (damage > 0) {
                targetHp -= damage;
            }

            if (healing > 0) {
                targetHp = System.Math.Min(targetHp + healing, _initialHp);
            }

            ApplyHp(targetHp, triggerHostDeath: true);
        }
    }

    /// <summary>
    /// Gets the health value from the locally active side of the entity.
    /// </summary>
    /// <returns>The current HP value.</returns>
    private int GetCurrentHp() {
        var healthManager = IsControlled ? _healthManager.Client : _healthManager.Host;
        return healthManager != null ? healthManager.hp : _lastHp;
    }

    /// <summary>
    /// Applies HP to both entity copies and optionally runs host death when a remote hit was lethal.
    /// </summary>
    /// <param name="newHp">The HP value to apply.</param>
    /// <param name="triggerHostDeath">Whether the scene host should run death when HP crosses zero.</param>
    private void ApplyHp(int newHp, bool triggerHostDeath) {
        var wasAlive = GetCurrentHp() > 0;

        _lastHp = newHp;

        if (_healthManager.Host != null) {
            _healthManager.Host.hp = newHp;
        }

        if (_healthManager.Client != null) {
            _healthManager.Client.hp = newHp;
        }

        if (triggerHostDeath && !IsControlled && wasAlive && newHp <= 0 && _healthManager.Host != null) {
            _healthManager.Host.Die(null, AttackTypes.Generic, true);
        }
    }

    /// <inheritdoc />
    public override void Destroy() {
        _healthManagerDieHook?.Dispose();
        _healthManagerDieHook = null;
        MonoBehaviourUtil.Instance.OnUpdateEvent -= OnUpdate;
    }
}
