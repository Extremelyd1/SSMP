using TMProOld;
using UnityEngine;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Ui.Resources;

/// <summary>
/// The font manager that stores fonts that are used in-game.
/// </summary>
internal static class FontManager {
    /// <summary>
    /// The font used for UI.
    /// </summary>
    public static Font UIFontRegular = null!;

    /// <summary>
    /// The font used for usernames above player objects.
    /// </summary>
    public static TMP_FontAsset InGameNameFont = null!;

    /// <summary>
    /// Load the fonts by trying to find them in the game through Unity.
    /// </summary>
    public static void LoadFonts() {
        Logger.Info("Loading fonts...");

        foreach (var font in UnityEngine.Resources.FindObjectsOfTypeAll<Font>()) {
            // Logger.Info($"Font: {font.name}");
            
            switch (font.name) {
                case "Perpetua":
                    UIFontRegular = font;
                    break;
            }
        }

        foreach (var textMeshProFont in UnityEngine.Resources.FindObjectsOfTypeAll<TMP_FontAsset>()) {
            // Logger.Info($"TMP_FontAsset: {textMeshProFont.name}");
            
            switch (textMeshProFont.name) {
                case "TrajanPro-Bold SDF":
                    InGameNameFont = textMeshProFont;
                    break;
            }
        }

        if (UIFontRegular == null) {
            Logger.Error("UI font regular is missing!");
        }

        if (InGameNameFont == null) {
            Logger.Error("In-game name font is missing!");
        }
    }
}
