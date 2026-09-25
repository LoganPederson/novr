using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Networking;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;

namespace NOVR.VrUi.HarmonyPatches;

// In flat-screen play, AllyInfo shows the player name and airframe of the friendly aircraft nearest the
// view direction. It only enables the label when that aircraft's HUD marker is within ~200px of the HUD
// centre, measured in canvas-local space. In VR the mod positions markers in world space on the HUD
// sphere, so the local-space distance is always huge and the label never appears.
//
// This patch re-implements the hover test against the VR target designator (the "selector" that follows
// head look) and places the label in world space next to the hovered marker, facing the HUD camera.
internal static class AllyInfoVrHoverPatch
{
    // Vanilla accepts markers within 200px of HUD centre. Markers sit on a sphere of radius HudDistance,
    // so 200 HUD units is roughly the same angular window (~11 degrees).
    private const float HoverRangeHudUnits = 200.0f;
    private const float HoverRangeSquared = HoverRangeHudUnits * HoverRangeHudUnits;

    private static readonly FieldInfo HoveredAllyInfoField = AccessTools.Field(typeof(global::AllyInfo), "hoveredAllyInfo");
    private static readonly FieldInfo HoverIconExistsField = AccessTools.Field(typeof(global::AllyInfo), "hoverIconExists");
    private static readonly FieldInfo HoveredAllyField = AccessTools.Field(typeof(global::AllyInfo), "hoveredAlly");
    private static readonly FieldInfo HoveredAllyMarkerField = AccessTools.Field(typeof(global::AllyInfo), "hoveredAllyMarker");

    private static bool _loggedHierarchy;

    private static bool IsVrHudActive() => APIBus.MainCamera != null && APIBus.CockpitHudCamera != null;

    // World-space point the player is "pointing" at on the HUD sphere: the target designator if it exists,
    // otherwise straight ahead of the HUD camera.
    private static Vector3 GetSelectorAnchor(global::CombatHUD combatHud, Camera cockpitHudCamera)
    {
        var designator = combatHud.targetDesignator;
        if (designator != null)
            return designator.transform.position;
        return cockpitHudCamera.transform.forward * VrHudProjectionHelper.HudDistance;
    }

    [HarmonyPatch(typeof(global::AllyInfo), "UpdateAllyInfoOnHover")]
    private static class UpdateAllyInfoOnHoverPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(global::AllyInfo __instance)
        {
            if (!IsVrHudActive())
                return true;

            var combatHud = SceneSingleton<global::CombatHUD>.i;
            var text = HoveredAllyInfoField.GetValue(__instance) as TextMeshProUGUI;
            if (combatHud == null || combatHud.aircraft == null || text == null)
                return false;

            var cockpitHudCamera = APIBus.CockpitHudCamera;
            var ownAircraft = combatHud.aircraft;
            var anchor = GetSelectorAnchor(combatHud, cockpitHudCamera);
            var previousAlly = HoveredAllyField.GetValue(__instance) as Aircraft;

            Aircraft? bestAlly = null;
            global::HUDUnitMarker? bestMarker = null;
            var bestSquared = HoverRangeSquared;

            IEnumerable<Aircraft> allies = ownAircraft.NetworkHQ.GetActiveAircraft(false);
            foreach (var ally in allies)
            {
                if (ally == null || ally == ownAircraft)
                    continue;
                if (!combatHud.TryGetMarker(ally, out var marker) || marker == null || marker.image == null || !marker.image.enabled)
                    continue;

                var squared = (marker.image.transform.position - anchor).sqrMagnitude;
                if (squared >= bestSquared)
                    continue;

                bestSquared = squared;
                bestAlly = ally;
                bestMarker = marker;
            }

            var hoverIconExists = bestAlly != null && bestMarker != null;
            HoveredAllyField.SetValue(__instance, bestAlly);
            HoveredAllyMarkerField.SetValue(__instance, bestMarker);
            HoverIconExistsField.SetValue(__instance, hoverIconExists);
            text.enabled = hoverIconExists;

            if (hoverIconExists && previousAlly != bestAlly)
            {
                text.text = "";
                if (bestAlly!.Player != null)
                    text.text += bestAlly.Player.GetDisplayName(PlayerNameContext.Other) + "\n";
                text.text += bestAlly.definition.code + "\n\n\n ";
                text.color = ThemeManager.Active.ColorTheme.HudUnitFriendly.WithAlpha(1.0f);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(global::AllyInfo), "LateUpdate")]
    private static class LateUpdatePatch
    {
        // Vanilla LateUpdate copies the marker's canvas-local position onto the label and disables the
        // label when the hovered ally is selected or the HUD is jammed. Let it run, then move the label
        // into VR world space if it is still enabled.
        [HarmonyPostfix]
        private static void Postfix(global::AllyInfo __instance)
        {
            if (!IsVrHudActive())
                return;

            var text = HoveredAllyInfoField.GetValue(__instance) as TextMeshProUGUI;
            if (text == null || !text.enabled)
                return;

            var marker = HoveredAllyMarkerField.GetValue(__instance) as global::HUDUnitMarker;
            if (marker == null || marker.image == null)
                return;

            var cockpitHudCamera = APIBus.CockpitHudCamera;
            var vrUiLayer = (int)LayerHelper.GetVrUiLayer();
            if (text.gameObject.layer != vrUiLayer)
                LayerHelper.SetLayerRecursive(text.transform, LayerHelper.GetVrUiLayer());

            if (!_loggedHierarchy)
            {
                _loggedHierarchy = true;
                var canvas = text.GetComponentInParent<Canvas>();
                Debug.Log($"AllyInfoVrHoverPatch: hoveredAllyInfo lives under canvas '{(canvas != null ? canvas.gameObject.name : "<none>")}' " +
                          $"(renderMode={(canvas != null ? canvas.renderMode.ToString() : "n/a")})");
            }

            text.transform.position = marker.image.transform.position;
            text.transform.rotation = cockpitHudCamera.transform.rotation;
        }
    }
}
