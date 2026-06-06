using UnityEngine;

namespace SSMP.Util;

/// <summary>
/// Associates an effect object with the player object it belongs to.
/// </summary>
public class EffectOwnerComponent : MonoBehaviour {
    /// <summary>
    /// The owner of the effect.
    /// </summary>
    public GameObject? Owner;
}
