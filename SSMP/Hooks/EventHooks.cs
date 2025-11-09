using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GlobalEnums;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using TeamCherry.Localization;

// ReSharper disable NotAccessedField.Local

namespace SSMP.Hooks;

/// <summary>
/// Static class that exposes simple to subscribe to events by using MonoDetour for the underlying hooking
/// functionality.
/// </summary>
public static class EventHooks {
    /// <summary>
    /// The binding flags for obtaining certain types for hooking.
    /// </summary>
    private const BindingFlags BindingFlags = System.Reflection.BindingFlags.Public |
                                              System.Reflection.BindingFlags.NonPublic |
                                              System.Reflection.BindingFlags.Instance;

    /// <summary>
    /// Hook for UIManager.Awake.
    /// </summary>
    private static Hook? _uiManagerAwakeHook;
    /// <summary>
    /// Hook for UIManager.SetState.
    /// </summary>
    private static Hook? _uiManagerSetStateHook;
    /// <summary>
    /// Hook for UIManager.UIGoToMainMenu.
    /// </summary>
    private static Hook? _uiManagerUIGoToMainMenuHook;
    /// <summary>
    /// Hook for UIManager.ReturnToMainMenu.
    /// </summary>
    private static Hook? _uiManagerReturnToMainMenuHook;

    /// <summary>
    /// Hook for Language.Has.
    /// </summary>
    private static Hook? _languageHasHook;
    /// <summary>
    /// Hook for Language.Get.
    /// </summary>
    private static Hook? _languageGetHook;

    /// <summary>
    /// Hook for GameManager.StartNewGame.
    /// </summary>
    private static Hook? _gameManagerStartNewGameHook;
    /// <summary>
    /// Hook for GameManager.ContinueGame.
    /// </summary>
    private static Hook? _gameManagerContinueGameHook;

    /// <summary>
    /// Hook for tk2dSpriteAnimator.Play.
    /// </summary>
    private static Hook? _spriteAnimatorPlayHook;
    /// <summary>
    /// Hook for tk2dSpriteAnimator.WarpClipToLocalTime.
    /// </summary>
    private static Hook? _spriteAnimatorWarpClipToLocalTimeHook;
    /// <summary>
    /// Hook for tk2dSpriteAnimator.ProcessEvents.
    /// </summary>
    private static Hook? _spriteAnimatorProcessEventsHook;

    /// <summary>
    /// Hook for HeroController.Update.
    /// </summary>
    private static Hook? _heroControllerUpdateHook;

    /// <summary>
    /// Hook for GameMap.PositionCompassAndCorpse.
    /// </summary>
    private static Hook? _gameMapPositionCompassAndCorpseHook;
    /// <summary>
    /// Hook for GameMap.CloseQuickMap.
    /// </summary>
    private static Hook? _gameMapCloseQuickMapHook;

    /// <summary>
    /// Hook for ToolItemManager.SetEquippedCrest.
    /// </summary>
    private static Hook? _toolItemManagerSetEquippedCrestHook;

    /// <summary>
    /// Hook for <see cref="CameraLockArea"/>.<see cref="CameraLockArea.Awake"/>.
    /// </summary>
    private static Hook? _cameraLockAreaAwakeHook;

    /// <summary>
    /// Hooks for InteractableBase.AddInside.
    /// </summary>
    private static Dictionary<Action<ILContext>, ILHook> _interactableBaseAddInsideHooks = new();
    /// <summary>
    /// Hooks for InteractableBase.LocalAddInside.
    /// </summary>
    private static Dictionary<Action<ILContext>, ILHook> _interactableBaseLocalAddInsideHooks = new();
    
    /// <summary>
    /// Hooks for TransitionPoint.OnTriggerEnter2D.
    /// </summary>
    private static Dictionary<Action<ILContext>, ILHook> _transitionPointOnTriggerEnter2DHooks = new();
    /// <summary>
    /// Hooks for TransitionPoint.OnTriggerStay2D.
    /// </summary>
    private static Dictionary<Action<ILContext>, ILHook> _transitionPointOnTriggerStay2DHooks = new();

    /// <summary>
    /// Event that is called after UIManager.SetState is called.
    /// </summary>
    public static event Action<UIManager, UIState>? UIManagerSetState;
    /// <summary>
    /// Event that is called after UIManager.UIGoToMainMenu is called.
    /// </summary>
    public static event Action? UIManagerUIGoToMainMenu;
    /// <summary>
    /// Event that is called after UIManager.ReturnToMainMenu is called.
    /// </summary>
    public static event Action? UIManagerReturnToMainMenu;

    /// <summary>
    /// Event that is called when Language.Has is called. Can be used to modify the return value of the call.
    /// </summary>
    public static event Func<string, string, bool?>? LanguageHas;
    /// <summary>
    /// Event that is called when Language.Get is called. Can be used to modify the return value of the call.
    /// </summary>
    public static event Func<string, string, string?>? LanguageGet;

    /// <summary>
    /// Event that is called when GameManager.StartNewGame is called.
    /// </summary>
    public static event Action? GameManagerStartNewGame;
    /// <summary>
    /// Event that is called when GameManager.ContinueGame is called.
    /// </summary>
    public static event Action? GameManagerContinueGame;

    /// <summary>
    /// Event that is called when tk2dSpriteAnimator.Play is called.
    /// </summary>
    public static event Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip, float, float>? SpriteAnimatorPlay;
    /// <summary>
    /// Event that is called when tk2dSpriteAnimator.WarpClipToLocalTime is called.
    /// </summary>
    public static event Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip, float>? SpriteAnimatorWarpClipToLocalTime;
    /// <summary>
    /// Event that is called when tk2dSpriteAnimator.ProcessEvents is called.
    /// </summary>
    public static event Action<tk2dSpriteAnimator, int, int, int>? SpriteAnimatorProcessEvents;

    /// <summary>
    /// Event that is called when HeroController.Update is called.
    /// </summary>
    public static event Action<HeroController>? HeroControllerUpdate;

    /// <summary>
    /// Event that is called when GameMap.PositionCompassAndCorpse is called.
    /// </summary>
    public static event Action<GameMap>? GameMapPositionCompassAndCorpse;
    /// <summary>
    /// Event that is called when GameMap.CloseQuickMap is called.
    /// </summary>
    public static event Action<GameMap>? GameMapCloseQuickMap;

    /// <summary>
    /// Event that is called when ToolItemManager.SetEquippedCrest is called.
    /// </summary>
    public static event Action<Action<string>, string>? ToolItemManagerSetEquippedCrest;

    /// <summary>
    /// Event that is called when <see cref="CameraLockArea"/>.<see cref="CameraLockArea.Awake"/> is called.
    /// </summary>
    public static event Action<CameraLockArea>? CameraLockAreaAwake;

    /// <summary>
    /// Event that is called when the IL for InteractableBase.AddInside is generated.
    /// </summary>
    public static event Action<ILContext>? InteractableBaseAddInsideIL {
        add => AddIlHookEvent(
            value, 
            typeof(InteractableBase).GetMethod(nameof(InteractableBase.AddInside), BindingFlags),
            _interactableBaseAddInsideHooks
        );
        remove => RemoveIlHookEvent(value, _interactableBaseAddInsideHooks);
    }
    /// <summary>
    /// Event that is called when the IL for InteractableBase.LocalAddInside is generated.
    /// </summary>
    public static event Action<ILContext>? InteractableBaseLocalAddInsideIL {
        add => AddIlHookEvent(
            value,
            typeof(InteractableBase).GetMethod(nameof(InteractableBase.LocalAddInside), BindingFlags),
            _interactableBaseLocalAddInsideHooks
        );
        remove => RemoveIlHookEvent(value, _interactableBaseLocalAddInsideHooks);
    }
    
    /// <summary>
    /// Event that is called when the IL for TransitionPoint.OnTriggerEnter2D is generated.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public static event Action<ILContext>? TransitionPointOnTriggerEnter2DIL {
        add => AddIlHookEvent(
            value, 
            typeof(TransitionPoint).GetMethod(nameof(TransitionPoint.OnTriggerEnter2D), BindingFlags),
            _transitionPointOnTriggerEnter2DHooks
        );
        remove => RemoveIlHookEvent(value, _transitionPointOnTriggerEnter2DHooks);
    }
    /// <summary>
    /// Event that is called when the IL for TransitionPoint.OnTriggerStay2D is generated.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public static event Action<ILContext>? TransitionPointOnTriggerStay2DIL {
        add => AddIlHookEvent(
            value, 
            typeof(TransitionPoint).GetMethod(nameof(TransitionPoint.OnTriggerStay2D), BindingFlags),
            _transitionPointOnTriggerStay2DHooks
        );
        remove => RemoveIlHookEvent(value, _transitionPointOnTriggerStay2DHooks);
    }

    /// <summary>
    /// Initializes all the hooks. 
    /// </summary>
    internal static void Initialize() {
        _uiManagerAwakeHook = new Hook(
            typeof(UIManager).GetMethod(nameof(UIManager.Awake), BindingFlags),
            OnUIManagerAwake
        );
        
        _uiManagerSetStateHook = new Hook(
            typeof(UIManager).GetMethod(nameof(UIManager.SetState)),
            OnUIManagerSetState
        );
        _uiManagerUIGoToMainMenuHook = new Hook(
            typeof(UIManager).GetMethod(nameof(UIManager.UIGoToMainMenu)),
            OnUIManagerUIGoToMainMenu
        );
        _uiManagerReturnToMainMenuHook = new Hook(
            typeof(UIManager).GetMethod(nameof(UIManager.ReturnToMainMenu)),
            OnUIManagerReturnToMainMenu
        );
        
        _gameManagerStartNewGameHook = new Hook(
            typeof(GameManager).GetMethod(nameof(GameManager.StartNewGame)),
            OnGameManagerStartNewGame
        );
        _gameManagerContinueGameHook = new Hook(
            typeof(GameManager).GetMethod(nameof(GameManager.ContinueGame)),
            OnGameManagerContinueGame
        );

        _spriteAnimatorPlayHook = new Hook(
            typeof(tk2dSpriteAnimator).GetMethod(
                nameof(tk2dSpriteAnimator.Play),
                [typeof(tk2dSpriteAnimationClip), typeof(float), typeof(float)]
            ),
            OnSpriteAnimatorPlay
        );
        _spriteAnimatorWarpClipToLocalTimeHook = new Hook(
            typeof(tk2dSpriteAnimator).GetMethod(
                nameof(tk2dSpriteAnimator.WarpClipToLocalTime), 
                BindingFlags
            ),
            OnSpriteAnimatorWarpClipToLocalTime
        );
        _spriteAnimatorProcessEventsHook = new Hook(
            typeof(tk2dSpriteAnimator).GetMethod(
                nameof(tk2dSpriteAnimator.ProcessEvents),
                BindingFlags
            ),
            OnSpriteAnimatorProcessEvents
        );

        _heroControllerUpdateHook = new Hook(
            typeof(HeroController).GetMethod(nameof(HeroController.Update), BindingFlags),
            OnHeroControllerUpdate
        );

        _gameMapPositionCompassAndCorpseHook = new Hook(
            typeof(GameMap).GetMethod(nameof(GameMap.PositionCompassAndCorpse)),
            OnGameMapPositionCompassAndCorpse
        );
        _gameMapCloseQuickMapHook = new Hook(
            typeof(GameMap).GetMethod(nameof(GameMap.CloseQuickMap)),
            OnGameMapCloseQuickMap
        );

        _toolItemManagerSetEquippedCrestHook = new Hook(
            typeof(ToolItemManager).GetMethod(nameof(ToolItemManager.SetEquippedCrest)),
            OnToolItemManagerSetEquippedCrest
        );

        _cameraLockAreaAwakeHook = new Hook(
            typeof(CameraLockArea).GetMethod(nameof(CameraLockArea.Awake), BindingFlags),
            OnCameraLockAreaAwake
        );
    }

    /// <summary>
    /// Add a subscriber to an IL Hook event with the given value for the event subscription and the given dictionary
    /// that backs the subscriptions.
    /// </summary>
    /// <param name="value">The action to invoke for modifying the IL.</param>
    /// <param name="methodBase">The method base to IL hook.</param>
    /// <param name="backingDict">The dictionary containing subscriptions for this event.</param>
    private static void AddIlHookEvent(
        Action<ILContext>? value, 
        MethodBase? methodBase, 
        Dictionary<Action<ILContext>, ILHook> backingDict
    ) {
        if (value == null || methodBase == null) {
            return;
        }
            
        if (backingDict.TryGetValue(value, out var hook)) {
            hook.Dispose();
        }

        backingDict[value] = new ILHook(
            methodBase, 
            value.Invoke
        );
    }

    /// <summary>
    /// Remove a subscriber from an IL Hook event with the given value for the event subscription and the given
    /// dictionary that backs the subscriptions.
    /// </summary>
    /// <param name="value">The action that was used for modifying the IL.</param>
    /// <param name="backingDict">The dictionary containing subscriptions for this event.</param>
    private static void RemoveIlHookEvent(
        Action<ILContext>? value,
        Dictionary<Action<ILContext>, ILHook> backingDict
    ) {
        if (value == null) {
            return;
        }

        if (backingDict.TryGetValue(value, out var hook)) {
            hook.Dispose();
            backingDict.Remove(value);
        }
    }

    private static void OnUIManagerAwake(Action<UIManager> orig, UIManager self) {
        orig(self);
        
        _languageHasHook = new Hook(
            typeof(Language).GetMethod("Has", [typeof(string), typeof(string)]),
            OnLanguageHas
        );
        _languageGetHook = new Hook(
            typeof(Language).GetMethod("Get", [typeof(string), typeof(string)]),
            OnLanguageGet
        );
    }

    private static void OnUIManagerSetState(Action<UIManager, UIState> orig, UIManager self, UIState state) {
        orig(self, state);

        UIManagerSetState?.Invoke(self, state);
    }

    private static void OnUIManagerUIGoToMainMenu(Action<UIManager> orig, UIManager self) {
        orig(self);

        UIManagerUIGoToMainMenu?.Invoke();
    }

    private static IEnumerator OnUIManagerReturnToMainMenu(Func<UIManager, IEnumerator> orig, UIManager self) {
        UIManagerReturnToMainMenu?.Invoke();

        return orig(self);
    }
    
    private static bool OnLanguageHas(Func<string, string, bool> orig, string key, string sheet) {
        var result = LanguageHas?.Invoke(key, sheet);
        return result ?? orig(key, sheet);
    }

    private static string OnLanguageGet(Func<string, string, string> orig, string key, string sheet) {
        var result = LanguageGet?.Invoke(key, sheet);
        return result ?? orig(key, sheet);
    }

    private static void OnGameManagerStartNewGame(Action<GameManager, bool, bool> orig, GameManager self,
        bool permaDeathMode, bool bossRushMode) {
        orig(self, permaDeathMode, bossRushMode);

        GameManagerStartNewGame?.Invoke();
    }

    private static void OnGameManagerContinueGame(Action<GameManager> orig, GameManager self) {
        orig(self);

        GameManagerContinueGame?.Invoke();
    }

    private static void OnSpriteAnimatorPlay(
        Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip, float, float> orig,
        tk2dSpriteAnimator self,
        tk2dSpriteAnimationClip clip,
        float clipStartTime,
        float overrideFps
    ) {
        orig(self, clip, clipStartTime, overrideFps);
        
        SpriteAnimatorPlay?.Invoke(self, clip, clipStartTime, overrideFps);
    }
    
    private static void OnSpriteAnimatorWarpClipToLocalTime(
        Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip, float> orig,
        tk2dSpriteAnimator self,
        tk2dSpriteAnimationClip clip,
        float time
    ) {
        orig(self, clip, time);
        
        SpriteAnimatorWarpClipToLocalTime?.Invoke(self, clip, time);
    }
    
    private static void OnSpriteAnimatorProcessEvents(
        Action<tk2dSpriteAnimator, int, int, int> orig,
        tk2dSpriteAnimator self,
        int start,
        int last,
        int direction
    ) {
        orig(self, start, last, direction);
        
        SpriteAnimatorProcessEvents?.Invoke(self, start, last, direction);
    }

    private static void OnHeroControllerUpdate(Action<HeroController> orig, HeroController self) {
        orig(self);

        HeroControllerUpdate?.Invoke(self);
    }

    private static void OnGameMapPositionCompassAndCorpse(Action<GameMap> orig, GameMap self) {
        orig(self);

        GameMapPositionCompassAndCorpse?.Invoke(self);
    }

    private static void OnGameMapCloseQuickMap(Action<GameMap> orig, GameMap self) {
        orig(self);

        GameMapCloseQuickMap?.Invoke(self);
    }

    private static void OnToolItemManagerSetEquippedCrest(Action<string> orig, string crestId) {
        if (ToolItemManagerSetEquippedCrest == null) {
            orig(crestId);
            return;
        }

        ToolItemManagerSetEquippedCrest.Invoke(orig, crestId);
    }

    private static void OnCameraLockAreaAwake(Action<CameraLockArea> orig, CameraLockArea self) {
        orig(self);
        
        CameraLockAreaAwake?.Invoke(self);
    }
}
