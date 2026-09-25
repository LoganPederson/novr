using NOVR.PatchHelper;
using UnityEngine;

namespace NOVR.Patches.Misc;

public class CameraPatch
{
    // Unity already prevents this for cameras rendering to the headset, but it also nags you constantly about it.
    // Some games try to change the FOV every frame, and all those logs can reduce performance.
    // Cameras that render to a texture (e.g. TargetCam) or are excluded from stereo still need their FOV to work.
    [PatchPrefix(typeof(Camera), "set_fieldOfView")]
    private static bool PreventChangingFov(Camera __instance)
    {
        return __instance.targetTexture != null || __instance.stereoTargetEye == StereoTargetEyeMask.None;
    }
}
