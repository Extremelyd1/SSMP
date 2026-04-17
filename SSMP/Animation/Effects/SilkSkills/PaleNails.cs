using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using SSMP.Internals;
using SSMP.Util;
using UnityEngine;
using Logger = SSMP.Logging.Logger;
using Object = UnityEngine.Object;

namespace SSMP.Animation.Effects.SilkSkills;

internal class PaleNails : BaseSilkSkill {

    private const string AnticName = "Hornet_finger_blade_cast_silk";

    private const string NailName = "Hornet Finger Blade {0}";

    private const int NailCount = 3;

    public bool IsAntic = false;

    private struct PlayerNails {
        public GameObject[] Trailing;
        public GameObject[] Firing;
    }

    private Dictionary<int, PlayerNails> _playerNails = new();

    public override void Play(GameObject playerObject, CrestType crestType, byte[]? effectInfo) {
        var isVolt = IsVolt(effectInfo);
        var isShaman = crestType == CrestType.Shaman;

        MonoBehaviourUtil.Instance.StartCoroutine(PlayAntic(playerObject.gameObject, isVolt, isShaman));
    }

    private IEnumerator PlayAntic(GameObject playerObject, bool isVolt, bool isShaman) {
        PlayHornetAttackSound(playerObject);

        var fsm = GetSkillFSM();

        // Play main antic
        if (TryGetAntic(playerObject, out var antic)) {
            antic.SetActive(false);
            antic.SetActive(true);

            var volt = antic
                .FindGameObjectInChildren("offset")?
                .FindGameObjectInChildren("zap");

            if (volt) {
                volt.SetActive(false);
                volt.SetActive(isVolt);
            }
        }

        // Play volt audio
        if (isVolt) {
            var voltAudio = fsm.GetFirstAction<PlayAudioEvent>("Boss Needle Zap FX");
            AudioUtil.PlayAudio(voltAudio, playerObject);
        }

        // Wait for animation to finish
        yield return new WaitForSeconds(0.2f);

        // Play summon audio
        var needleAudio = fsm.GetFirstAction<PlayAudioEvent>("BossNeedle Cast");
        AudioUtil.PlayAudio(needleAudio, playerObject);

        var localNail = fsm.GetFirstAction<SpawnObjectFromGlobalPool>("BossNeedle Cast");

        // Summon nails
        var nails = new GameObject[NailCount];

        for (var i = 0; i < NailCount; i++) {
            var nail = EffectUtils.SpawnGlobalPoolObject(localNail, playerObject.transform, 10)!;
            nail.name = string.Format(NailName, i);

            nails[i] = nail;
        }

        // Set up FSMs for each nail
        for (var i = 0; i < NailCount; i++) {
            SetupNailFsm(playerObject, nails, i);
        }

        var id = playerObject.GetInstanceID();
        if (!_playerNails.TryGetValue(id, out var playerNails)) {
            playerNails = new PlayerNails();
        }

        playerNails.Trailing = nails;

        _playerNails[id] = playerNails;
    }

    private void SetupNailFsm(GameObject playerObject, GameObject[] nails, int index) {
        var nail = nails[index];
        var fsm = nail.LocateMyFSM("Control");
        if (fsm == null) return;

        FixFsmForUse(fsm, playerObject);

        string position;
        GameObject buddy1;
        GameObject buddy2;

        if (index == 0) {
            position = "TOP1";
            buddy1 = nails[1];
            buddy2 = nails[2];
        } else if (index == 1) {
            position = "MID1";
            buddy1 = nails[0];
            buddy2 = nails[2];
        } else {
            position = "BOT1";
            buddy1 = nails[0];
            buddy2 = nails[1];
        }

        fsm.Fsm.Variables.GetFsmGameObject("Buddy 1").Value = buddy1;
        fsm.Fsm.Variables.GetFsmGameObject("Buddy 2").Value = buddy2;

        fsm.Fsm.Event(position);
    }

    private bool FixFsmForUse(PlayMakerFSM fsm, GameObject playerObject) {
        //fsm.Init();

        fsm.enabled = false;

        const string followLeftName = "Follow HeroFacingLeft";
        const string followRightName = "Follow HeroFacingRight";

        // Set FSM variables
        var target = fsm.FsmVariables.FindFsmGameObject("Target");
        target.Value = playerObject;

        var wallTrueVar = new FsmFloat { Value = 1 };

        var wallTrackCount = new FsmInt { Value = 0 };
        var wallTrackTest = new FsmEnum { Value = Extensions.IntTest.LessThan };


        // Set offset
        if (playerObject.transform.GetScaleX() == -1) {
            var setAngle = fsm.GetFirstAction<SetFloatValue>("Set Top 1");
            setAngle?.floatValue = 180 - setAngle.floatValue.Value;

            setAngle = fsm.GetFirstAction<SetFloatValue>("Set Mid 1");
            setAngle?.floatValue = 180 - setAngle.floatValue.Value;

            setAngle = fsm.GetFirstAction<SetFloatValue>("Set Bot 1");
            setAngle?.floatValue = 180 - setAngle.floatValue.Value;
        }
        

        // Set follow targets
        var flyTo = fsm.GetFirstAction<DirectlyFlyTo>(followLeftName);
        flyTo?.target = target;

        flyTo = fsm.GetFirstAction<DirectlyFlyTo>(followRightName);
        flyTo?.target = target;

        // Set scale target
        var getScale = fsm.GetAction<GetScale>(followLeftName, 4);
        getScale?.gameObject.gameObject = target;

        getScale = fsm.GetAction<GetScale>(followLeftName, 5);
        getScale?.gameObject.gameObject = target;

        getScale = fsm.GetAction<GetScale>(followRightName, 4);
        getScale?.gameObject.gameObject = target;

        getScale = fsm.GetAction<GetScale>(followRightName, 5);
        getScale?.gameObject.gameObject = target;

        // Remove wall checks
        var wallCheck = fsm.GetAction<ConvertBoolToFloat>(followLeftName, 7);
        wallCheck?.trueValue = wallTrueVar;

        wallCheck = fsm.GetAction<ConvertBoolToFloat>(followRightName, 7);
        wallCheck?.trueValue = wallTrueVar;

        // Remove track trigger
        var trackTrigger = fsm.GetAction<CheckTrackTriggerCountV2>(followLeftName, 12);

        trackTrigger?.Count = wallTrackCount;
        trackTrigger?.Test = wallTrackTest;

        trackTrigger = fsm.GetAction<CheckTrackTriggerCountV2>(followRightName, 12);
        trackTrigger?.Count = wallTrackCount;
        trackTrigger?.Test = wallTrackTest;

        // Remove transitions
        var left = fsm.GetState(followLeftName);
        left.Transitions = [
            left.Transitions[0],
            left.Transitions[5]
        ];

        var right = fsm.GetState(followRightName);
        right.Transitions = [
            right.Transitions[0],
            right.Transitions[5],
        ];

        fsm.enabled = true;
        return true;
    }

    private GameObject GetNailParent(GameObject playerObject) {
        var attacks = GetPlayerSilkAttacks(playerObject);

        const string parentName = "Pale Nails";
        var nails = attacks.FindGameObjectInChildren(parentName);
        if (nails == null) {
            nails = new GameObject(parentName);
            nails.transform.SetParentReset(attacks.transform);
        }

        return nails;
    }

    private bool TryGetAntic(GameObject playerObject, [MaybeNullWhen(false)] out GameObject antic) {
        // Find existing first
        var effects = playerObject.FindGameObjectInChildren("Effects");
        if (effects == null) {
            antic = null;
            return false;
        }

        antic = effects.FindGameObjectInChildren(AnticName);
        if (antic != null) {
            return true;
        }

        var localAntic = HeroController.instance.gameObject
            .FindGameObjectInChildren("Effects")?
            .FindGameObjectInChildren(AnticName);

        if (localAntic == null) {
            return false;
        }

        antic = Object.Instantiate(localAntic, effects.transform);
        antic.name = AnticName;

        antic.DestroyComponent<ToolEquipChecker>();

        return true;
    }
}
