using HutongGames.PlayMaker.Actions;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;

namespace SSMP.Animation.Effects.Tools;

/// <summary>
/// Class for the tool effect of the Threefold Pin.
/// </summary>
internal class ThreefoldPin : BaseAttackTool {
    /// <summary>
    /// Cached prefab for one attacking Threefold Pin.
    /// </summary>
    private static GameObject? _modifiedPrefab;

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        var poisoned = EffectIsPoisoned(effectInfo);
        var fsm = HeroController.instance.toolsFSM;

        // Set up modified prefab
        if (!_modifiedPrefab) {
            var prefab = fsm.GetFirstAction<SpawnObjectFromGlobalPool>("TriPin Ground L");

            _modifiedPrefab = EffectUtils.SpawnGlobalPoolObject(prefab, playerObject.transform, 0);
            if (!_modifiedPrefab) return;

            _modifiedPrefab.SetActive(false);
            _modifiedPrefab.name = "THREEFOLD PIN";
        }

        // Play audio
        var audio = fsm.GetFirstAction<PlayAudioEventRandom>("TriPin Type");
        AudioUtil.PlayAudio(audio, playerObject);

        // Spawn pins
        var isOnWall = EffectIsOnWall(effectInfo);
        SpawnPin(playerObject, 177, 0, isOnWall, poisoned);
        SpawnPin(playerObject, 167, -10, isOnWall, poisoned);
        SpawnPin(playerObject, 157, -20, isOnWall, poisoned);
    }

    /// <summary>
    /// Spawns a pin at a given player object.
    /// </summary>
    /// <param name="playerObject">The player using the pin.</param>
    /// <param name="minAngle">The minimum angle for the pin.</param>
    /// <param name="rotation">The pin's rotation.</param>
    /// <param name="onWall">If the player is on a wall or not.</param>
    /// <param name="poisoned">If the pin is poisoned or not.</param>
    private void SpawnPin(GameObject playerObject, float minAngle, float rotation, bool onWall, bool poisoned) {
        // Determine spawn position
        var spawnPosition = playerObject.transform.position;
        var playerFacing = playerObject.transform.localScale.x;
        if (onWall) {
            spawnPosition.x += 0.5f * playerFacing;
        } else {
            spawnPosition.x -= 1.1f * playerFacing;
        }

        // Spawn pin
        var pin = _modifiedPrefab.Spawn(spawnPosition);
        if (!pin) return;

        // Set up variables, then change depending on facing direction
        var directionToSet = 180;
        var scaleX = -1.2f;
        var maxAngle = minAngle + 3;

        var facingDirection = playerObject.transform.localScale.x * (onWall ? -1 : 1);
        if (facingDirection < 0) {
            directionToSet = 0;
            scaleX = 1.2f;
            maxAngle = 180 - minAngle;
            minAngle = maxAngle - 3;
            rotation *= -1;
        }

        // Set direction and rotation
        pin.SendMessage("SetDirection", directionToSet, SendMessageOptions.DontRequireReceiver);

        pin.transform.SetLocalRotation2D(rotation);
        pin.transform.localScale = new Vector3(scaleX, 1, 1);

        // Set initial velocity
        if (!pin.TryGetComponent<Rigidbody2D>(out var body)) {
            return;
        }

        var angle = Random.Range(minAngle, maxAngle);
        var x = 60 * Mathf.Cos(angle * (Mathf.PI / 180f));
        var y = 60 * Mathf.Sin(angle * (Mathf.PI / 180f));

        body.linearVelocity = new Vector2(x, y);

        SetDamageHeroState(pin, ServerSettings.ThreefoldPinDamage);

        // Set poison settings and deflection
        if (pin.TryGetComponent<ToolPin>(out var controller)) {
            StraightPin.SetPinPoison(controller, poisoned);

            // Allows deflecting pins, but causes some side effects that make it look a bit worse (disappears
            // immediately after hitting walls)
            controller.tinked = ServerSettings.IsPvpEnabled && ShouldDoDamage;
        }
    }
}
