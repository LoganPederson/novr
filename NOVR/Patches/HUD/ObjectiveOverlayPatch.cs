using System.Reflection;
using HarmonyLib;
using NOVR.PatchHelper;
using NOVR.VrUi.HarmonyPatches;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOVR.Patches.HUD;

internal static class ObjectiveOverlayPatch
{
    private static readonly FieldInfo ObjectivePointerField = AccessTools.Field(typeof(ObjectiveOverlay), "objectivePointer");
    private static readonly FieldInfo ObjectiveDotField = AccessTools.Field(typeof(ObjectiveOverlay), "objectiveDot");
    private static readonly FieldInfo SizeIndicatorField = AccessTools.Field(typeof(ObjectiveOverlay), "sizeIndicator");
    private static readonly FieldInfo ObjectiveInfoField = AccessTools.Field(typeof(ObjectiveOverlay), "objectiveInfo");
    private static readonly FieldInfo PointerTailField = AccessTools.Field(typeof(ObjectiveOverlay), "pointerTail");
    private static readonly FieldInfo HiddenField = AccessTools.Field(typeof(ObjectiveOverlay), "hidden");

    [PatchPrefix(typeof(ObjectiveOverlay), nameof(ObjectiveOverlay.UpdateOverlay))]
    private static bool UpdateOverlay(ObjectiveOverlay __instance, MissionPosition.PositionResult result)
    {
        var mainCamera = APIBus.MainCamera;
        var cockpitHudCamera = APIBus.CockpitHudCamera;
        if (mainCamera == null || cockpitHudCamera == null)
            return true;

        // Use "as" so a type change in a game update falls back to vanilla instead of throwing every frame.
        var objectivePointer = ObjectivePointerField?.GetValue(__instance) as Image;
        var objectiveDot = ObjectiveDotField?.GetValue(__instance) as Image;
        var sizeIndicator = SizeIndicatorField?.GetValue(__instance) as Image;
        // TextMeshProUGUI since Nuclear Option 0.34 (was UnityEngine.UI.Text).
        var objectiveInfo = ObjectiveInfoField?.GetValue(__instance) as TMP_Text;
        var pointerTail = PointerTailField?.GetValue(__instance) as Transform;

        if (objectivePointer == null || objectiveDot == null || sizeIndicator == null || objectiveInfo == null || pointerTail == null)
            return true;

        HiddenField?.SetValue(__instance, false);
        objectivePointer.enabled = true;
        objectiveInfo.enabled = true;

        var worldPosition = result.Position.ToLocalPosition();
        var isOffScreen = VrHudProjectionHelper.PinToScreenEdge(worldPosition, out var hudPosition, out var arrowAngle);
        var angleToTarget = Vector3.Angle(mainCamera.transform.forward, result.Direction);

        objectivePointer.transform.position = hudPosition;
        objectiveDot.transform.position = hudPosition;

        if (isOffScreen)
        {
            sizeIndicator.enabled = false;
            objectivePointer.transform.localEulerAngles = new Vector3(0f, 0f, arrowAngle * Mathf.Rad2Deg - 90f);
        }
        else
        {
            sizeIndicator.enabled = true;
        }

        if (angleToTarget > 10f)
        {
            objectivePointer.enabled = true;
            objectiveDot.enabled = false;
            __instance.TextNoOverlap.SetTarget(pointerTail.position);
        }
        else
        {
            objectivePointer.enabled = false;
            objectiveDot.enabled = true;
            __instance.TextNoOverlap.SetTarget(objectiveDot.transform.position - Vector3.up * 25f);
        }

        var range = result.Range.GetValueOrDefault();
        var invDistance = 1f / (result.Distance != 0f ? result.Distance : 0.01f);
        sizeIndicator.transform.localScale = Vector3.one * (35f * range * invDistance);
        var sizeRangeFactor = range * 20f * invDistance - 0.5f;
        sizeIndicator.transform.localEulerAngles = Vector3.forward * sizeRangeFactor * 3f;
        // Like vanilla since 0.34: keep the indicator colour (set by SetColor) and only fade its alpha.
        sizeIndicator.color = sizeIndicator.color.WithAlpha(Mathf.Clamp01(sizeRangeFactor));
        sizeIndicator.transform.position = objectivePointer.transform.position;

        var label = result.Objective?.SavedObjective.DisplayName ?? "Waypoint";
        objectiveInfo.text = $"{label} {UnitConverter.DistanceReading(result.Distance)}";
        objectiveInfo.fontSize = (int)PlayerSettings.overlayTextSize;

        return false;
    }
}
