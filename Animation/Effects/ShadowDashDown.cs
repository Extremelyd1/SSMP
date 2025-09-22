using UnityEngine;

namespace SSMP.Animation.Effects;

/// <summary>
/// Animation effect class for the downwards Shadow Dash.
/// </summary>
internal class ShadowDashDown : DashBase {
    /// <inheritdoc/>
    public override void Play(GameObject playerObject, bool[] effectInfo) {
        Play(playerObject, effectInfo, true, false, true);
    }
}
