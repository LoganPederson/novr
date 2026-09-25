using NOVR.VrUi.HarmonyPatches;
using UnityEngine;

namespace NOVR.VrUi.SpecialBehavior;

public class NOVRStatusDisplayBehavior : UIRenderedCanvasBehavior
{
    private static readonly Vector3 HudOffset = new(630f, 145f, 0f);
    private Vector3? _baseScale;

    private void Update()
    {
        _baseScale ??= transform.localScale;
        transform.position = HudInfoPanelSettings.ApplySpread(HudOffset) + new Vector3(0f, 0f, VrHudProjectionHelper.HudDistance);
        transform.localScale = _baseScale.Value * HudInfoPanelSettings.Scale;
    }
}
