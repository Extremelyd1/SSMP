using System;
using SSMP.Networking.Client;
using UnityEngine;

#pragma warning disable CS8603 // Possible null reference return.

namespace SSMP.Game.Client.Entity.Component;

/// <summary>
/// Factory class that instantiates <see cref="EntityComponent"/> by type and additional parameters.
/// </summary>
internal static class ComponentFactory {
    /// <summary>
    /// Instantiate an <see cref="EntityComponent"/> by their type.
    /// </summary>
    /// <param name="type">The type of the component.</param>
    /// <param name="netClient">The net client for passing to the constructor of the component.</param>
    /// <param name="entityId">The entity ID for passing to the constructor of the component.</param>
    /// <param name="objects">The host and client objects for passing to the constructor of the component.</param>
    /// <returns>The instantiated entity component.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the type is not one that can be instantiated
    /// here.</exception>
    public static EntityComponent InstantiateByType(
        EntityComponentType type,
        NetClient netClient,
        ushort entityId,
        HostClientPair<GameObject> objects
    ) {
        switch (type) {
            case EntityComponentType.Rotation:
                return new RotationComponent(netClient, entityId, objects);
            case EntityComponentType.Velocity:
                return new VelocityComponent(netClient, entityId, objects, GetHost<Rigidbody2D>(objects));
            case EntityComponentType.GravityScale:
                return new GravityScaleComponent(netClient, entityId, objects, GetHost<Rigidbody2D>(objects));
            case EntityComponentType.ZPosition:
                return new ZPositionComponent(netClient, entityId, objects);
            case EntityComponentType.EnemySpawner:
                return new EnemySpawnerComponent(netClient, entityId, objects, GetPair<EnemySpawner>(objects));
            case EntityComponentType.ChildrenActivation:
                return new ChildrenActivationComponent(netClient, entityId, objects);
            case EntityComponentType.SpriteRenderer:
                return new SpriteRendererComponent(netClient, entityId, objects, GetPair<SpriteRenderer>(objects));
            case EntityComponentType.ChallengePrompt:
                return new ChallengePromptComponent(netClient, entityId, objects);
            case EntityComponentType.Music:
                return MusicComponent.CreateInstance(netClient, entityId, objects, out var musicComponent)
                    ? musicComponent
                    : null;
            case EntityComponentType.DreamPlatform:
                return new DreamPlatformComponent(netClient, entityId, objects);
            case EntityComponentType.HazardRespawn:
                return new HazardRespawnComponent(netClient, entityId, objects);
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(type), type, $"Could not instantiate entity component for type: {type}"
                );
        }
    }

    private static T GetHost<T>(HostClientPair<GameObject> objects) where T : UnityEngine.Component =>
        objects.Host.TryGetComponent<T>(out var component) ? component : null;

    private static HostClientPair<T> GetPair<T>(HostClientPair<GameObject> objects) where T : UnityEngine.Component =>
        new() {
            Client = objects.Client.TryGetComponent<T>(out var client) ? client : null!,
            Host = objects.Host.TryGetComponent<T>(out var host) ? host : null!
        };
}
