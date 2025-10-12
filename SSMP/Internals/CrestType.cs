using System;

namespace SSMP.Internals;

/// <summary>
/// Enumeration of Crest types.
/// </summary>
public enum CrestType : byte {
    Hunter = 0,
    HunterV2,
    HunterV3,
    Reaper,
    Wanderer,
    Beast,
    Witch,
    Architect,
    Shaman,
    Cursed,
    Cloakless
}

/// <summary>
/// Extension methods for the CrestType enumeration to go from internal name to enum and vice versa.
/// </summary>
public static class CrestTypeExt {
    /// <summary>
    /// Get the internal crest ID from the crest type.
    /// </summary>
    /// <param name="crestType">The crest type.</param>
    /// <returns>The internal crest ID as a string as used in Silksong's code.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the given crest type does not have an associated
    /// internal name.</exception>
    public static string ToInternal(this CrestType crestType) {
        switch (crestType) {
            case CrestType.Hunter:
                return "Hunter";
            case CrestType.HunterV2:
                return "Hunter_v2";
            case CrestType.HunterV3:
                return "Hunter_v3";
            case CrestType.Reaper:
                return "Reaper";
            case CrestType.Wanderer:
                return "Wanderer";
            case CrestType.Beast:
                return "Warrior";
            case CrestType.Witch:
                return "Witch";
            case CrestType.Architect:
                return "Toolmaster";
            case CrestType.Shaman:
                return "Spell";
            case CrestType.Cursed:
                return "Cursed";
            case CrestType.Cloakless:
                return "Cloakless";
            default:
                throw new InvalidOperationException("Supplied CrestType does not exist!");
        }
    }

    /// <summary>
    /// Get the crest type from the given internal name.
    /// </summary>
    /// <param name="crestId">The internal crest ID as a string.</param>
    /// <returns>The corresponding crest type.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the given crest ID does not have an associated crest
    /// type.</exception>
    public static CrestType FromInternal(string crestId) {
        switch (crestId) {
            case "Hunter":
                return CrestType.Hunter;
            case "Hunter_v2":
                return CrestType.HunterV2;
            case "Hunter_v3":
                return CrestType.HunterV3;
            case "Reaper":
                return CrestType.Reaper;
            case "Wanderer":
                return CrestType.Wanderer;
            case "Warrior":
                return CrestType.Beast;
            case "Witch":
                return CrestType.Witch;
            case "Toolmaster":
                return CrestType.Architect;
            case "Spell":
                return CrestType.Shaman;
            case "Cursed":
                return CrestType.Cursed;
            case "Cloakless":
                return CrestType.Cloakless;
            default:
                throw new InvalidOperationException($"Supplied crestType (\"{crestId}\") does not exist!");
        }
    }

    /// <summary>
    /// Check whether the crest type is a form of the Hunter crest. For example, one of the upgraded Hunter crest
    /// types.
    /// </summary>
    /// <param name="crestType">The crest type.</param>
    /// <returns>True if the crest type is a Hunter crest, otherwise false.</returns>
    public static bool IsHunter(this CrestType crestType) {
        return crestType is CrestType.Hunter or CrestType.HunterV2 or CrestType.HunterV3;
    }
}
