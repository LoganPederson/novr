using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace NOVR.GameplayPatches;

// TargetCam (the in-cockpit "locked target" screen) renders to a plain RenderTexture and was
// never meant to render to the headset, but its prefab leaves Camera.stereoTargetEye = Both.
// Forcing it to None is correct regardless of the workaround below - a texture-target camera
// shouldn't be flagged for stereo/XR rendering once NOVR enables global XR.
[HarmonyPatch(typeof(TargetCam), "Initialize")]
internal static class TargetCamStereoFix
{
      private static readonly AccessTools.FieldRef<TargetCam, Camera> CamField =
                AccessTools.FieldRefAccess<TargetCam, Camera>("cam");

      private static readonly AccessTools.FieldRef<TargetCam, Camera> UiCamField =
                AccessTools.FieldRefAccess<TargetCam, Camera>("UICam");

      [HarmonyPostfix]
      private static void Postfix(TargetCam __instance)
      {
                var cam = CamField(__instance);
                if (cam != null)
                {
                              cam.stereoTargetEye = StereoTargetEyeMask.None;
                }

                var uiCam = UiCamField(__instance);
                if (uiCam != null)
                {
                              uiCam.stereoTargetEye = StereoTargetEyeMask.None;
                }
      }
}

// Workaround for TargetCam's zoom never applying (GitHub issue InfernoSuperNova/novr#25).
// The root cause was NOVR's own CameraPatches prefix, which used to block Camera.fieldOfView
// writes on every camera. That prefix now lets texture-target cameras through, so the game's
// own targetFOV -> cam.fieldOfView lerp should work again; this projection-matrix path is kept
// until that is confirmed in a headset, and can then be deleted.
[HarmonyPatch(typeof(TargetCam), "Update")]
internal static class TargetCamZoomWorkaround
{
      private static readonly AccessTools.FieldRef<TargetCam, Camera> CamField =
                AccessTools.FieldRefAccess<TargetCam, Camera>("cam");

      private static readonly AccessTools.FieldRef<TargetCam, float> TargetFovField =
                AccessTools.FieldRefAccess<TargetCam, float>("targetFOV");

      // Our own "current FOV" per TargetCam instance. Weakly keyed so destroyed TargetCams
      // (one per aircraft spawn) don't accumulate for the whole session.
      private static readonly ConditionalWeakTable<TargetCam, StrongBox<float>> CurrentFov = new();

      [HarmonyPostfix]
      private static void Postfix(TargetCam __instance)
      {
                var cam = CamField(__instance);
                if (cam == null || !cam.enabled) return;

                var targetFov = TargetFovField(__instance);

                if (!CurrentFov.TryGetValue(__instance, out var current))
                {
                              current = new StrongBox<float>(targetFov);
                              CurrentFov.Add(__instance, current);
                }

                current.Value = Mathf.Lerp(current.Value, targetFov, Time.deltaTime);

                cam.projectionMatrix = Matrix4x4.Perspective(current.Value, cam.aspect, cam.nearClipPlane, cam.farClipPlane);
      }
}
