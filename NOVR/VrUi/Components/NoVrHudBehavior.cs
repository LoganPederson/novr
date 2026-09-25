using NOVR.VrUi.HarmonyPatches;
using UnityEngine;

namespace NOVR.VrUi.SpecialBehavior;

public class NoVrHudBehavior : UIRenderedCanvasBehavior
{
    private void Update()
    {
        transform.position = new Vector3(0f, 0f, VrHudProjectionHelper.HudDistance);
        transform.rotation = Quaternion.identity;
    }
}