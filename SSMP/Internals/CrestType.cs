using System;

namespace SSMP.Internals;

/// <summary>
/// Enumeration of Crest types.
/// </summary>
public enum CrestType : byte {
    Hunter = 0,
    Reaper,
    Wanderer,
    Beast,
    Witch,
    Architect,
    Shaman,
}

/// <summary>
/// Extension methods for the CrestType enumeration to go from internal name to enum and vice versa.
/// </summary>
public static class CrestTypeExt {
    public static string ToInternal(this CrestType crestType) {
        switch (crestType) {
            case CrestType.Hunter:
                return "Hunter";
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
            default:
                throw new InvalidOperationException("Supplied CrestType does not exist!");
        }
    }

    public static CrestType FromInternal(string crestType) {
        switch (crestType) {
            case "Hunter":
            case "Hunter_v2":
            case "Hunter_v3":
                return CrestType.Hunter;
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
            default:
                throw new InvalidOperationException($"Supplied crestType (\"{crestType}\") does not exist!");
        }
    }
}
