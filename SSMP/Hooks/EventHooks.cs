using System;
using System.Collections;
using System.Reflection;
using GlobalEnums;
using MonoMod.RuntimeDetour;
using SSMP.Logging;
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

    private static Hook? _uiManagerAwakeHook;
    private static Hook? _uiManagerSetStateHook;
    private static Hook? _uiManagerUIGoToMainMenuHook;
    private static Hook? _uiManagerReturnToMainMenuHook;

    private static Hook? _languageHasHook;
    private static Hook? _languageGetHook;

    private static Hook? _gameManagerStartNewGameHook;
    private static Hook? _gameManagerContinueGameHook;

    private static Hook? _spriteAnimatorPlayHook;
    private static Hook? _spriteAnimatorWarpClipToLocalTimeHook;
    private static Hook? _spriteAnimatorProcessEventsHook;

    private static Hook? _heroControllerUpdateHook;

    private static Hook? _gameMapPositionCompassAndCorpseHook;
    private static Hook? _gameMapCloseQuickMapHook;

    private static Hook? _toolItemManagerSetEquippedCrestHook;

    public static event Action<UIManager, UIState>? UIManagerSetStatePostFix;
    public static event Action? UIManagerUIGoToMainMenu;
    public static event Action? UIManagerReturnToMainMenu;

    public static event Func<string, string, bool?>? LanguageHas;
    public static event Func<string, string, string?>? LanguageGet;

    public static event Action? GameManagerStartNewGame;
    public static event Action? GameManagerContinueGame;

    public static event Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip, float, float>? SpriteAnimatorPlay;
    public static event Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip, float>? SpriteAnimatorWarpClipToLocalTime;
    public static event Action<tk2dSpriteAnimator, int, int, int>? SpriteAnimatorProcessEvents;
    
    public static event Action<HeroController>? HeroControllerUpdate;

    public static event Action<GameMap>? GameMapPositionCompassAndCorpse;
    public static event Action<GameMap>? GameMapCloseQuickMap;

    public static event Action<Action<string>, string>? ToolItemManagerSetEquippedCrest;

    public static void Initialize() {
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
    }

    private static void OnUIManagerAwake(Action<UIManager> orig, UIManager self) {
        orig(self);
        
        Logger.Info("OnUIManagerAwake");

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

        Logger.Message($"OnUIManagerSetState: {state}");

        UIManagerSetStatePostFix?.Invoke(self, state);
    }

    private static void OnUIManagerUIGoToMainMenu(Action<UIManager> orig, UIManager self) {
        orig(self);

        Logger.Message("OnUIManagerUIGoToMainMenu");

        UIManagerUIGoToMainMenu?.Invoke();
    }

    private static IEnumerator OnUIManagerReturnToMainMenu(Func<UIManager, IEnumerator> orig, UIManager self) {
        Logger.Message("OnUIManagerReturnToMainMenu");

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
        ToolItemManagerSetEquippedCrest?.Invoke(orig, crestId);
    }
}
