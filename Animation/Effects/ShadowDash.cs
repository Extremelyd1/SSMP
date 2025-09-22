using UnityEngine;

namespace SSMP.Animation.Effects;

/// <summary>
/// Animation effect class for the Shadow Dash ability.
/// </summary>
internal class ShadowDash : DashBase {
    /// <inheritdoc/>
    public override void Play(GameObject playerObject, bool[] effectInfo) {
        Play(playerObject, effectInfo, true, false, false);
    }
}
