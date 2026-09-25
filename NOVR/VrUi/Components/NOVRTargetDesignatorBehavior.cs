using NOVR.VrUi.HarmonyPatches;
using UnityEngine;

namespace NOVR.VrUi.SpecialBehavior;

public class NOVRTargetDesignatorBehavior : UIRenderedCanvasBehavior
{
    private const float DefaultOvershoot = 1.2f;

    private void Update()
    {
        var uiCam = APIBus.CockpitHudReference;
        var overshoot = Mathf.Clamp(ModConfiguration.Instance?.TargetDesignatorOvershoot.Value ?? DefaultOvershoot, 1.0f, 2.0f);

        transform.rotation = Quaternion.SlerpUnclamped(Quaternion.identity, uiCam.transform.rotation, overshoot);
        transform.position = transform.forward * VrHudProjectionHelper.HudDistance;
    }
}
