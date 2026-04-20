using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using HutongGames.PlayMaker.Actions;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects.SilkSkills;

internal class RuneRage : BaseSilkSkill {
    /// <summary>
    /// Name of the game object for the normal antic.
    /// </summary>
    private const string AnticName = "attack_bomb_cast_antic_thread";

    /// <summary>
    /// Name of the game object for the volt filament antic.
    /// </summary>
    private const string AnticVoltName = "antic_thread_zap";

    /// <summary>
    /// Scale used to keep a higher level of precision when converting a float to a byte
    /// </summary>
    private const int PositionScale = 9;

    /// <summary>
    /// Offset to keep negative values when converting an sbyte to a byte
    /// </summary>
    private const int PositionOffset = sbyte.MaxValue;


    /// <summary>
    /// Keeps track of if this instance is made to run the antic or not.
    /// </summary>
    public bool IsAntic = false;

    /// <summary>
    /// Cached object for Hornet's normal Rune Rage burst
    /// </summary>
    private static GameObject? _localRuneBlast;

    /// <summary>
    /// Cached object for Hornet's volt filament Rune Rage burst
    /// </summary>
    private static GameObject? _localRuneBlastVolt;

    /// <summary>
    /// Cached transform of an object that templates how a Rune Rage Cluster should be laid out
    /// </summary>
    private static Transform? _clusterSpawnTemplate;

    /// <inheritdoc/>
    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        var isVolt = IsVolt(effectInfo);
        var isShaman = crestType == CrestType.Shaman;

        // Play antic if appropriate
        if (IsAntic) {
            PlayRageAntic(playerObject, isVolt);
            return;
        }

        // Decode rune positions from effect info
        var positions = DecodeRunePositions(effectInfo, playerObject);
        
        // Check volt status
        if (effectInfo?.Length > 0 && effectInfo[0] == 1) {
            isVolt = true;
        }

        PlaySonar(playerObject, isVolt, isShaman);

        // There are runes to spawn. Do at the same time as the sonar.
        if (positions.Count > 0) {
            PlayRuneRage(positions, isVolt, isShaman);
        }
    }

    /// <summary>
    /// Plays the silk-based antic for Rune Rage
    /// </summary>
    /// <param name="playerObject">The player using the effect</param>
    /// <param name="isVolt">If the volt filament effects should be used</param>
    private static void PlayRageAntic(GameObject playerObject, bool isVolt) {
        PlayHornetAttackSound(playerObject);

        // Play antic
        if (TryGetAntic(playerObject, out var antic)) {
            antic.SetActive(false);
            antic.SetActive(true);

            var volt = antic.FindGameObjectInChildren(AnticVoltName);
            if (volt != null) {
                volt.SetActive(false);
                volt.SetActive(isVolt);
            }
        }

        var fsm = GetSkillFSM();
        
        // Play volt audio
        if (isVolt) {
            var voltAntic = fsm.GetFirstAction<PlayAudioEvent>("S Bomb Zap FX");
            if (voltAntic != null) {
                AudioUtil.PlayAudio(voltAntic, playerObject);
            }
        }

        // Play normal audio
        var runeAnticAudio = fsm.GetAction<PlayAudioEvent>("Silk Bomb Start", 15);
        if (runeAnticAudio != null) {
            AudioUtil.PlayAudio(runeAnticAudio, playerObject);
        }
    }

    /// <summary>
    /// Plays the sonar blast effect. Purely visual.
    /// </summary>
    /// <param name="playerObject">The player using the effect</param>
    /// <param name="isVolt">If the volt filament effects should be used</param>
    /// <param name="isShaman">If the shaman crest effects should be used</param>
    private void PlaySonar(GameObject playerObject, bool isVolt, bool isShaman) {
        var fsm = GetSkillFSM();

        // Play general audio
        var runeBurstAudio = fsm.GetFirstAction<PlayAudioEvent>("Initial Silk Cost");
        if (runeBurstAudio != null) {
            AudioUtil.PlayAudio(runeBurstAudio, playerObject);
        }

        // Play volt audio
        if (isVolt) {
            var zapAudioBug = fsm.GetFirstAction<PlayAudioEvent>("S Bomb Zap FX 2");
            if (zapAudioBug != null) {
                AudioUtil.PlayAudio(zapAudioBug, playerObject);
            }
        }

        // Spawn sonar, picking the right one for the volt filament setting
        var sonarPicker = fsm.GetFirstAction<BoolTestToGameObject>("Sonar Cast Effects");
        if (sonarPicker == null) return;

        GameObject localSonar;

        if (isVolt) {
            localSonar = sonarPicker.TrueGameObject.Value;
        } else {
            localSonar = sonarPicker.FalseGameObject.Value;
        }

        // Spawn and remove components
        var sonarParent = EffectUtils.SpawnGlobalPoolObject(localSonar, playerObject.transform, 2f);
        if (sonarParent != null) {
            sonarParent.DestroyComponentsInChildren<CameraControlAnimationEvents>();
        }

        if (!isShaman) return;

        // Spawn shaman effect and remove components
        var localShaman = fsm.GetAction<SpawnObjectFromGlobalPool>("Sonar Cast Effects", 6);
        if (localShaman == null) return;

        var shaman = EffectUtils.SpawnGlobalPoolObject(localShaman, playerObject.transform, 0.7f);
        if (shaman == null) return;

        shaman.DestroyComponent<HeroShamanRuneEffect>();
        shaman.DestroyComponent<CameraControlAnimationEvents>();
    }

    /// <summary>
    /// Spawns clusters of Rune Rage blasts at the given positions
    /// </summary>
    /// <param name="positions">The positions to spawn blast clusters at</param>
    /// <param name="isVolt">If the volt filament effects should be used</param>
    /// <param name="isShaman">If the shaman crest effects should be used</param>
    private void PlayRuneRage(List<Vector3> positions, bool isVolt, bool isShaman) {
        // Generate spawn template
        // Template layout kinda looks like . * .
        if (_clusterSpawnTemplate == null) {
            _clusterSpawnTemplate = new GameObject().transform;
            var firstBlast = new GameObject().transform;
            var secondBlast = new GameObject().transform;
            var thirdBlast = new GameObject().transform;

            firstBlast.SetParentReset(_clusterSpawnTemplate);
            firstBlast.localPosition = new Vector3(1.732f, -1, 0);
            firstBlast.SetRotation2D(240);

            secondBlast.SetParentReset(_clusterSpawnTemplate);
            secondBlast.SetLocalPositionY(2);

            thirdBlast.SetParent(_clusterSpawnTemplate);
            thirdBlast.localPosition = new Vector3(-1.732f, -1, 0);
            thirdBlast.SetRotation2D(-240);

            Object.DontDestroyOnLoad(_clusterSpawnTemplate.gameObject);
        }

        // Spawn clusters at each position
        foreach (var position in positions) {
            MonoBehaviourUtil.Instance.StartCoroutine(PlayRuneCluster(position, isVolt, isShaman));
        }
    }

    /// <summary>
    /// Gets the Rune Rage antic effect
    /// </summary>
    /// <param name="playerObject">The player that is using the antic</param>
    /// <param name="antic">The found or created antic</param>
    /// <returns></returns>
    private static bool TryGetAntic(GameObject playerObject, [MaybeNullWhen(false)] out GameObject antic) {
        // Find or create the antic object
        var created = FindOrCreateSkill(playerObject, AnticName, out var anticObj);
        if (anticObj == null) {
            antic = null;
            return false;
        }

        antic = anticObj;
        if (!created) return true;

        // Remove problematic component from newly created object
        antic.DestroyComponent<ToolEquipChecker>();

        return true;
    }

    /// <summary>
    /// Gets the prefab for a single Rune Rage blast
    /// </summary>
    /// <param name="isVolt">If the volt filament effects should be used</param>
    /// <returns>The blast prefab, if found.</returns>
    private static GameObject? TryGetLocalBlast(bool isVolt) {
        // Return existing if possible
        if (isVolt) {
            if (_localRuneBlastVolt) return _localRuneBlastVolt;
        } else if (_localRuneBlast) {
            return _localRuneBlast;
        }

        // Get the rune cluster object from the FSM
        var fsm = GetSkillFSM();
        var cluster = fsm.GetFirstAction<SpawnObjectFromGlobalPool>("Blast Enemy").gameObject.Value;

        if (cluster == null) return null;

        // Find the individual rune from within the cluster FSM
        var clusterFsm = cluster.LocateMyFSM("Control");
        if (clusterFsm == null) return null;

        var blaster = clusterFsm.GetFirstAction<BoolTestToGameObject>("Do Explosions");
        if (blaster == null) return null;

        // Fill out both blast prefabs
        _localRuneBlastVolt = blaster.TrueGameObject.Value;
        _localRuneBlast = blaster.FalseGameObject.Value;

        // Return correct blast
        if (isVolt) {
            return _localRuneBlastVolt;
        } else {
            return _localRuneBlast;
        }
    }

    /// <summary>
    /// Spawns a cluster of three Rune Rage blasts
    /// </summary>
    /// <param name="position">The initial position to spawn the cluster</param>
    /// <param name="isVolt">If the volt filament effects should be used</param>
    /// <param name="isShaman">If the shaman crest effects should be used</param>
    private IEnumerator PlayRuneCluster(Vector3 position, bool isVolt, bool isShaman) {
        if (!_clusterSpawnTemplate) yield break;

        // Create local version of template and set the position
        var spawnTemplate = Object.Instantiate(_clusterSpawnTemplate);
        spawnTemplate.position = position;

        // Change the local rotation for a bit more variety
        spawnTemplate.SetLocalRotation2D(Random.Range(0, 360));

        var offset = new Vector3(1.75f, 1.75f);

        // Spawn 3 runes per cluster, each one with a random offset from their spawn point
        for (var i = 0; i < spawnTemplate.childCount; i++) {
            var blastPosition = spawnTemplate.GetChild(i).position;
            blastPosition += offset.RandomInRange();

            CreateBlast(spawnTemplate, isVolt, isShaman, blastPosition);

            // Delay next rune by a little bit
            var waitTime = Random.Range(0.15f, 0.2f);
            yield return new WaitForSeconds(waitTime);
        }

        Object.Destroy(spawnTemplate.gameObject);
    }

    /// <summary>
    /// Spawns a single Rune Rage blast
    /// </summary>
    /// <param name="spawnTransform">The initial parent to use for spawning</param>
    /// <param name="isVolt">If the volt filament effects should be used</param>
    /// <param name="isShaman">If the shaman crest effects should be used</param>
    /// <param name="position">The final position of the blast</param>
    private void CreateBlast(Transform spawnTransform, bool isVolt, bool isShaman, Vector3 position) {
        var localBlast = TryGetLocalBlast(isVolt);
        if (localBlast == null) return;

        // Create copy of blast and set position
        var blast = EffectUtils.SpawnGlobalPoolObject(localBlast, spawnTransform, 3);
        if (blast == null) return;

        blast.transform.position = position;

        // Add damager
        var damager = blast
            .FindGameObjectInChildren("Blast")?
            .FindGameObjectInChildren("damager");

        if (damager != null) {
            SetDamageHeroStateCalculated(damager, ServerSettings.RuneRageDamage, isVolt, isShaman);
        }

        // Remove extra recycling component
        if (blast.TryGetComponent<RecycleResetHandler>(out var recycler)) {
            recycler.resetActions = null;
        }

        // Set shaman effect
        if (blast.TryGetComponent<HeroShamanRuneEffect>(out var rune)) {
            var runeObj = rune.rune;
            Object.DestroyImmediate(rune);

            if (runeObj) {
                runeObj.SetActive(isShaman);

                // Remove extra bloom object
                if (isShaman) {
                    var bloom = runeObj.FindGameObjectInChildren("Shaman Rune Camera Bloom");
                    if (bloom) {
                        bloom.SetActive(false);
                    }
                }
            }

        }

        // Remove camera controller
        blast.DestroyComponentsInChildren<CameraControlAnimationEvents>();

        // Cull while offscreen (fixes bug)
        var animator = blast.GetComponentInChildren<Animator>();
        if (animator) {
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        // Remove recycle action
        var fsm = blast.LocateMyFSM("Control");
        var end = fsm.GetState("End");
        end.Actions = [];
        end.SaveActions();
    }

    /// <summary>
    /// Converts a Vector3 to an array of bytes that are within a margin of error of the original X and Y values.
    /// To be used for encoding Rune Rage cluster positions
    /// </summary>
    /// <param name="runePosition">The position of the Rune Rage cluster</param>
    /// <returns></returns>
    internal static byte[] EncodeRunePosition(Vector3 runePosition) {
        var hornetPosition = HeroController.instance.transform.position;

        // The position of a cluster is always clamped to within +- ~13 units of Hornet.
        // Get position relative to the player and offset by max value of sbyte.
        // This allows us to keep negative values while using a byte.
        // Multiplying by a larger number also allows higher precision.
        var diffX = (byte)((runePosition.x - hornetPosition.x) * PositionScale + PositionOffset);
        var diffY = (byte)((runePosition.y - hornetPosition.y) * PositionScale + PositionOffset);

        return [
            diffX,
            diffY
        ];
    }

    /// <summary>
    /// Converts an array of bytes to Vector3s, using the reverse of the algorithm in <see cref="EncodeRunePosition"/>.
    /// To be used for decoding Rune Rage cluster positions
    /// </summary>
    /// <param name="info">The raw positions in byte form</param>
    /// <param name="playerObject">The player that used the effect. Cluster positions are relative to this player.</param>
    /// <returns></returns>
    private static List<Vector3> DecodeRunePositions(byte[]? info, GameObject playerObject) {
        if (info == null || info.Length < 3) {
            return [];
        }

        var positions = new List<Vector3>();
        var playerPosition = playerObject.transform.position;

        // Loop through all xy pairs
        for (var i = 1; i < info.Length - 1; i += 2) {
            // Restore sbyte from byte, then convert to float for division
            var x = (float)info[i] - PositionOffset;
            var y = (float)info[i + 1] - PositionOffset;

            // Restore original scale
            var position = new Vector3(x / PositionScale, y / PositionScale, 0) ;
            
            // Convert relative position to global position
            positions.Add(position + playerPosition);
        }

        return positions;
    }
}
