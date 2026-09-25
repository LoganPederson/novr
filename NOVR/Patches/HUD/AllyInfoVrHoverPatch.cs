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
//
// Vanilla only labels friendly aircraft. With "Show Enemy Type On Hover" enabled, hostile units are labelled
// too, with the same type name the target screen shows once they're locked.
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
    private static readonly AccessTools.FieldRef<global::CombatHUD, List<global::HUDUnitMarker>> MarkersField =
        AccessTools.FieldRefAccess<global::CombatHUD, List<global::HUDUnitMarker>>("markers");

    // The unit whose label text was last written, so the text is only rebuilt when the hovered unit changes.
    private static global::Unit? _labelledUnit;
    private static bool _loggedHierarchy;

    private static bool IsVrHudActive() => APIBus.MainCamera != null && APIBus.CockpitHudCamera != null;

    private static bool ShowEnemies => ModConfiguration.Instance?.ShowEnemyTypeOnHover.Value ?? true;

    // World-space point the player is "pointing" at on the HUD sphere: the target designator if it exists,
    // otherwise straight ahead of the HUD camera.
    private static Vector3 GetSelectorAnchor(global::CombatHUD combatHud, Camera cockpitHudCamera)
    {
        var designator = combatHud.targetDesignator;
        if (designator != null)
            return designator.transform.position;
        return cockpitHudCamera.transform.forward * VrHudProjectionHelper.HudDistance;
    }

    // Friendly aircraft always (as vanilla); hostile units when enabled. Missiles and neutral units are skipped.
    private static bool IsLabelled(global::Unit unit, global::Aircraft ownAircraft, bool showEnemies)
    {
        if (unit == null || unit == ownAircraft || unit is global::Missile || unit.NetworkHQ == null)
            return false;
        if (unit.NetworkHQ == ownAircraft.NetworkHQ)
            return unit is Aircraft;
        return showEnemies;
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
            var showEnemies = ShowEnemies;

            global::HUDUnitMarker? bestMarker = null;
            var bestSquared = HoverRangeSquared;

            var markers = MarkersField(combatHud);
            if (markers != null)
            {
                foreach (var marker in markers)
                {
                    if (marker == null || marker.image == null || !marker.image.enabled)
                        continue;
                    if (!IsLabelled(marker.unit, ownAircraft, showEnemies))
                        continue;

                    var squared = (marker.image.transform.position - anchor).sqrMagnitude;
                    if (squared >= bestSquared)
                        continue;

                    bestSquared = squared;
                    bestMarker = marker;
                }
            }

            var hoverIconExists = bestMarker != null;
            var hoveredUnit = bestMarker?.unit;
            // hoveredAlly is typed Aircraft; vanilla only reads it for the next hover pass.
            HoveredAllyField.SetValue(__instance, hoveredUnit as Aircraft);
            HoveredAllyMarkerField.SetValue(__instance, bestMarker);
            HoverIconExistsField.SetValue(__instance, hoverIconExists);
            text.enabled = hoverIconExists;

            if (hoverIconExists && hoveredUnit != _labelledUnit)
            {
                text.text = BuildLabel(hoveredUnit!, ownAircraft);
                var colorTheme = ThemeManager.Active.ColorTheme;
                var color = hoveredUnit!.NetworkHQ == ownAircraft.NetworkHQ ? colorTheme.HudUnitFriendly : colorTheme.HudUnitHostile;
                text.color = color.WithAlpha(1.0f);
            }

            _labelledUnit = hoverIconExists ? hoveredUnit : null;
            return false;
        }

        private static string BuildLabel(global::Unit unit, global::Aircraft ownAircraft)
        {
            // Friendly: player name and airframe code, exactly as vanilla.
            if (unit.NetworkHQ == ownAircraft.NetworkHQ && unit is Aircraft ally)
            {
                var label = "";
                if (ally.Player != null)
                    label += ally.Player.GetDisplayName(PlayerNameContext.Other) + "\n";
                return label + ally.definition.code + "\n\n\n ";
            }

            // Hostile: the type name the target screen shows for a locked target.
            var typeName = unit is Aircraft ? unit.definition.unitName : unit.unitName;
            return typeName + "\n\n\n ";
        }
    }

    [HarmonyPatch(typeof(global::AllyInfo), "LateUpdate")]
    private static class LateUpdatePatch
    {
        // Vanilla LateUpdate copies the marker's canvas-local position onto the label and disables the
        // label when the hovered unit is selected or the HUD is jammed. Let it run, then move the label
        // into VR world space if it is still enabled.
        [HarmonyPostfix]
        private static void Postfix(global::AllyInfo __instance)
        {
            if (!IsVrHudActive())
                return;

            var text = HoveredAllyInfoField.GetValue(__instance) as TextMeshProUGUI;
            if (text == null || !text.enabled)
            {
                // Vanilla hid the label (hovered unit locked, jammed, or gone); relabel when it next shows.
                _labelledUnit = null;
                return;
            }

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
