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

/// <inheritdoc />
/// This component manages the <see cref="HealthManager"/> component of the entity.
internal class HealthManagerComponent : EntityComponent {
    private const BindingFlags HookBindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const float ControlledHealCorrectionDelaySeconds = 0.4f;

    /// <summary>
    /// Host-client pair of health manager components of the entity.
    /// </summary>
    private readonly HostClientPair<HealthManager> _healthManager;

    /// <summary>
    /// Host death effects used to capture the emitted corpse snapshot.
    /// </summary>
    private readonly EnemyDeathEffects? _hostDeathEffects;

    /// <summary>
    /// Client death effects used to enable local physics on emitted corpses.
    /// </summary>
    private readonly EnemyDeathEffects? _clientDeathEffects;

    /// <summary>
    /// The host corpse emitted by the current death call.
    /// </summary>
    private GameObject? _hostCorpse;

    /// <summary>
    /// The client corpse emitted by the current death call.
    /// </summary>
    private GameObject? _clientCorpse;

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
    /// Whether a controlled client-side HP increase should be rolled back to the last authoritative value.
    /// </summary>
    private bool _hasPendingControlledHealCorrection;

    /// <summary>
    /// Unscaled time at which the next controlled heal correction should be applied.
    /// </summary>
    private float _pendingControlledHealCorrectionAt;

    public HealthManagerComponent(
        NetClient netClient,
        ushort entityId,
        HostClientPair<GameObject> gameObject,
        HostClientPair<HealthManager> healthManager
    ) : base(netClient, entityId, gameObject) {
        _healthManager = healthManager;

        _hostDeathEffects = gameObject.Host.GetComponent<EnemyDeathEffects>();
        if (_hostDeathEffects != null) {
            _hostDeathEffects.CorpseEmitted += OnHostCorpseEmitted;
        }

        _clientDeathEffects = gameObject.Client.GetComponent<EnemyDeathEffects>();
        if (_clientDeathEffects != null) {
            _clientDeathEffects.CorpseEmitted += OnClientCorpseEmitted;
        }

        _lastInvincible = healthManager.Host.IsInvincible;
        _lastHp = healthManager.Host.hp;
        _lastInvincibleFromDirection = healthManager.Host.InvincibleFromDirection;

        // Get the largest overload of Die from HealthManager, because that is the method that is getting called by
        // all other overloads regardless
        var dieMethod = typeof(HealthManager).GetMethod(
            nameof(HealthManager.Die),
            HookBindingFlags,
            Type.DefaultBinder,
            [
                typeof(float?), typeof(AttackTypes), typeof(NailElements), typeof(GameObject),
                typeof(bool), typeof(float), typeof(bool), typeof(bool)
            ],
            null
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
    /// Captures the corpse emitted by the host death lifecycle.
    /// </summary>
    private void OnHostCorpseEmitted(GameObject corpse) {
        _hostCorpse = corpse;
    }

    /// <summary>
    /// Enables local physics for a remote corpse and all of its body parts.
    /// </summary>
    private void OnClientCorpseEmitted(GameObject corpse) {
        if (corpse == null) {
            return;
        }

        _clientCorpse = corpse;
        RestoreCorpsePhysics(corpse);
    }

    /// <summary>
    /// Restores dynamic simulation for a corpse and all of its body parts on the local client.
    /// </summary>
    /// <param name="corpse">The emitted corpse to make physics-driven.</param>
    private static void RestoreCorpsePhysics(GameObject corpse) {
        if (corpse == null) {
            return;
        }

        foreach (var rigidbody in corpse.GetComponentsInChildren<Rigidbody2D>(true)) {
            if (rigidbody == null) {
                continue;
            }

            rigidbody.simulated = true;
            rigidbody.bodyType = RigidbodyType2D.Dynamic;
        }
    }

    /// <summary>
    /// Applies the host corpse's initial transform and velocity to the local corpse.
    /// </summary>
    /// <param name="corpse">The local corpse receiving the snapshot.</param>
    /// <param name="position">The host corpse's world position.</param>
    /// <param name="rotation">The host corpse's world rotation around the z-axis.</param>
    /// <param name="velocity">The host corpse's initial linear velocity.</param>
    private static void ApplyCorpseSnapshot(
        GameObject corpse,
        Vector2 position,
        float rotation,
        Vector2 velocity
    ) {
        if (corpse == null) {
            return;
        }

        var transform = corpse.transform;
        transform.position = new Vector3(position.x, position.y, transform.position.z);
        transform.rotation = Quaternion.Euler(0f, 0f, rotation);

        var rigidbody = corpse.GetComponent<Rigidbody2D>();
        if (rigidbody == null) {
            return;
        }

        rigidbody.position = position;
        rigidbody.rotation = rotation;
        rigidbody.linearVelocity = velocity;
    }

    /// <summary>
    /// Callback method for when the health manager dies.
    /// </summary>
    private void HealthManagerOnDie(
        Action<HealthManager, float?, AttackTypes, NailElements, GameObject, bool, float, bool, bool> orig,
        HealthManager self,
        float? attackDirection,
        AttackTypes attackType,
        NailElements nailElements,
        GameObject gameObject,
        bool ignoreEvasion,
        float corpseFlingMultiplier,
        bool overrideSpecialDeath,
        bool disallowDropFlying
    ) {
        if (self != _healthManager.Host && self != _healthManager.Client) {
            InvokeOrig();
            return;
        }

        if (self == _healthManager.Client) {
            if (!_allowDeath) {
                Logger.Info("HealthManager Die was called on client entity");
            } else {
                Logger.Info("HealthManager Die was called on client entity, but it is allowed death");

                InvokeOrig();

                _allowDeath = false;
            }

            return;
        }

        Logger.Info("HealthManager Die was called on host entity");

        _hostCorpse = null;
        InvokeOrig();

        var data = ObjectPool<EntityNetworkData>.Get();
        data.Type = EntityComponentType.Death;

        if (attackDirection.HasValue) {
            data.Packet.Write(true);
            data.Packet.Write(attackDirection.Value);
        } else {
            data.Packet.Write(false);
        }

        data.Packet.Write((byte) attackType);

        data.Packet.Write(ignoreEvasion);

        data.Packet.Write(_hostCorpse != null);
        if (_hostCorpse != null) {
            var corpsePosition = _hostCorpse.transform.position;
            var corpseRigidbody = _hostCorpse.GetComponent<Rigidbody2D>();

            data.Packet.Write(corpsePosition.x);
            data.Packet.Write(corpsePosition.y);
            data.Packet.Write(_hostCorpse.transform.eulerAngles.z);
            data.Packet.Write(corpseRigidbody?.linearVelocity.x ?? 0f);
            data.Packet.Write(corpseRigidbody?.linearVelocity.y ?? 0f);
        }

        _hostCorpse = null;

        SendData(data);
        return;

        // Utility method to invoke the original method with all the original arguments
        void InvokeOrig() {
            orig(
                self, attackDirection, attackType, nailElements, gameObject, ignoreEvasion, corpseFlingMultiplier,
                overrideSpecialDeath, disallowDropFlying
            );
        }
    }

    /// <summary>
    /// Callback method for updates to check whether health or invincibility changes.
    /// </summary>
    /// <inheritdoc />
    public override void OnUpdate() {
        var observedHealthManager = IsControlled ? _healthManager.Client : _healthManager.Host;
        if (observedHealthManager == null) {
            return;
        }

        var newHp = observedHealthManager.hp;
        if (newHp != _lastHp) {
            if (IsControlled && newHp > _lastHp) {
                if (_hasPendingControlledHealCorrection) {
                    if (Time.unscaledTime >= _pendingControlledHealCorrectionAt) {
                        _hasPendingControlledHealCorrection = false;
                        ApplyHp(_lastHp, triggerHostDeath: false);
                    }
                } else {
                    _hasPendingControlledHealCorrection = true;
                    _pendingControlledHealCorrectionAt = Time.unscaledTime + ControlledHealCorrectionDelaySeconds;
                }

                return;
            }

            _hasPendingControlledHealCorrection = false;
            var previousHp = _lastHp;
            _lastHp = newHp;

            // Obtain a pooled network data instance to avoid new allocations
            var hpData = ObjectPool<EntityNetworkData>.Get();
            hpData.Type = EntityComponentType.Health;

            hpData.Packet.Write(previousHp);
            hpData.Packet.Write(newHp);
            hpData.Packet.Write(_currentHealthEpoch);

            SendData(hpData);
        }

        var newInvincible = _healthManager.Host.IsInvincible;
        var newInvincibleFromDir = _healthManager.Host.InvincibleFromDirection;

        // Only retrieve and populate invincibilityData from the pool if a value has actually changed,
        // preventing unnecessary allocations on 99.9% of frames.
        if (newInvincible != _lastInvincible || newInvincibleFromDir != _lastInvincibleFromDirection) {
            _lastInvincible = newInvincible;
            _lastInvincibleFromDirection = newInvincibleFromDir;

            var invincibilityData = ObjectPool<EntityNetworkData>.Get();
            invincibilityData.Type = EntityComponentType.Invincibility;

            invincibilityData.Packet.Write(newInvincible);
            invincibilityData.Packet.Write((byte) newInvincibleFromDir);

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

    /// <summary>
    /// Resets health epoch tracking for a new scene-host epoch.
    /// </summary>
    /// <param name="sceneHostEpoch">The new scene-host epoch assigned by the server.</param>
    private void ResetHealthOrderingForEpoch(uint sceneHostEpoch) {
        _currentHealthEpoch = sceneHostEpoch;
        _lastReceivedHealthEpoch = sceneHostEpoch;
    }

    /// <inheritdoc />
    public override void Update(EntityNetworkData data, bool alreadyInSceneUpdate) {
        Logger.Info("Received health manager update");

        if (!IsControlled && data.Type != EntityComponentType.Health) {
            Logger.Info("  Entity was not controlled");
            return;
        }

        switch (data.Type) {
            case EntityComponentType.Death: {
                var attackDirection = new float?();
                if (data.Packet.ReadBool()) {
                    attackDirection = data.Packet.ReadFloat();
                }

                var attackType = (AttackTypes) data.Packet.ReadByte();
                var ignoreEvasion = data.Packet.ReadBool();

                var hasCorpseSnapshot = data.Packet.ReadBool();
                var corpsePosition = Vector2.zero;
                var corpseRotation = 0f;
                var corpseVelocity = Vector2.zero;

                if (hasCorpseSnapshot) {
                    corpsePosition = new Vector2(data.Packet.ReadFloat(), data.Packet.ReadFloat());
                    corpseRotation = data.Packet.ReadFloat();
                    corpseVelocity = new Vector2(data.Packet.ReadFloat(), data.Packet.ReadFloat());
                }

                // Set a boolean to indicate that the client health manager is allowed to execute the Die method
                _allowDeath = true;
                _clientCorpse = null;
                _healthManager.Client.Die(attackDirection, attackType, ignoreEvasion);

                if (hasCorpseSnapshot && _clientCorpse != null) {
                    ApplyCorpseSnapshot(_clientCorpse, corpsePosition, corpseRotation, corpseVelocity);
                }

                break;
            }
            case EntityComponentType.Health:
                UpdateHealth(data, alreadyInSceneUpdate);
                break;
            case EntityComponentType.Invincibility: {
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
                break;
            }
        }
    }

    /// <summary>
    /// Applies a health update from the network.
    /// Scene snapshots are applied as absolute HP,
    /// while live updates are merged as HP deltas
    /// so delayed packets do not overwrite local damage or healing.
    /// </summary>
    private void UpdateHealth(EntityNetworkData data, bool alreadyInSceneUpdate) {
        _hasPendingControlledHealCorrection = false;

        var previousHp = data.Packet.ReadInt();
        var newHp = data.Packet.ReadInt();
        var healthEpoch = data.Packet.ReadUInt();

        if (alreadyInSceneUpdate) {
            ResetHealthOrderingForEpoch(healthEpoch);
            ApplyHp(newHp, triggerHostDeath: false);
        } else {
            // Cache once; avoids re-evaluating the IsControlled check twice below on every packet
            var isControlled = IsControlled;

            if (healthEpoch < _lastReceivedHealthEpoch) {
                return;
            }

            if (healthEpoch > _lastReceivedHealthEpoch) {
                _lastReceivedHealthEpoch = healthEpoch;
                if (isControlled) {
                    _currentHealthEpoch = healthEpoch;
                }
            }

            var currentHp = isControlled ? _lastHp : GetCurrentHp();
            var damage = System.Math.Max(previousHp - newHp, 0);
            var healing = System.Math.Max(newHp - previousHp, 0);

            var targetHp = currentHp;
            if (damage > 0) {
                targetHp -= damage;
            }

            if (healing > 0) {
                targetHp += healing;
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
        if (_hostDeathEffects != null) {
            _hostDeathEffects.CorpseEmitted -= OnHostCorpseEmitted;
        }

        if (_clientDeathEffects != null) {
            _clientDeathEffects.CorpseEmitted -= OnClientCorpseEmitted;
        }

        _healthManagerDieHook?.Dispose();
        _healthManagerDieHook = null;
        MonoBehaviourUtil.Instance.OnUpdateEvent -= OnUpdate;
    }
}
