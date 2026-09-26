using System;
using System.Collections;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using NOVR.VrUi.SpecialBehavior;
using UnityEngine;
using UnityEngine.UI;

namespace NOVR.Compat;

// Nuclear Option eXtensions (NOX, https://github.com/lunaboards-dev/Nuclear-Option-Extensions).
// No license is published for NOX, so nothing of it is copied here; this only moves its objects.
//
// RWR display (UI/RWRDisplay.cs, Hooks/RWRHud.cs): parented to the HeadMountedDisplay rect, anchored
// to its top-right corner and placed under "TopRightPanel" every Update. In VR that corner sits far in
// the periphery of NOVR's head-locked HMD, NOVR has already moved TopRightPanel onto the HUD (so NOX
// may never find it and throw every frame), and contact lines use world eulerAngles, which only line
// up on an unrotated parent. The display is re-parented to the cockpit-fixed HUD centre next to the
// panel NOVR moved, with a position and scale that can be changed in NOVR's config.
//
// Squad labels (UI/HUDMarker.cs): positioned from the marker in Update, but CombatHUD moves markers in
// LateUpdate and NOVR moves them with the head, so labels trail a frame behind and ignore the marker's
// billboard rotation. They are snapped to their marker after CombatHUD.LateUpdate.
internal static class NoxCompat
{
    internal const string PluginGuid = "NOX";

    private const string ConfigSection = "Compat - NOX";

    private static readonly CompatGuard RwrGuard = new("NOX RWR display");
    private static readonly CompatGuard LabelGuard = new("NOX squad labels");
    private static readonly AccessTools.FieldRef<CombatHUD, GameObject> TopRightPanel =
        AccessTools.FieldRefAccess<CombatHUD, GameObject>("topRightPanel");

    private static FieldInfo? _rwrTransformField;
    private static FieldInfo? _rwrTopRightPanelField;
    private static Type? _labelType;
    private static FieldInfo? _labelListField;
    private static FieldInfo? _labelParentField;
    private static FieldInfo? _labelTextField;
    private static FieldInfo? _nameOffsetField;

    private static ConfigEntry<bool>? _moveRwr;
    private static ConfigEntry<float>? _rwrX;
    private static ConfigEntry<float>? _rwrY;
    private static ConfigEntry<float>? _rwrScale;

    internal static void Apply(Harmony harmony)
    {
        BindConfig();

        var rwrType = AccessTools.TypeByName("NOX.UI.RWRDisplay");
        _rwrTransformField = AccessTools.Field(rwrType, "Tf");
        _rwrTopRightPanelField = AccessTools.Field(rwrType, "TRPanel");
        var rwrUpdate = AccessTools.Method(rwrType, "Update");
        if (rwrUpdate != null && _rwrTransformField != null && _rwrTopRightPanelField != null)
        {
            harmony.Patch(rwrUpdate,
                prefix: new HarmonyMethod(typeof(NoxCompat), nameof(RwrPrefix)),
                postfix: new HarmonyMethod(typeof(NoxCompat), nameof(RwrPostfix)));
        }
        else
        {
            NOVRLog.Warning("Compat: NOX RWRDisplay members not found; RWR display left as is.");
        }

        _labelType = AccessTools.TypeByName("NOX.UI.HUDMarker");
        _labelListField = AccessTools.Field(_labelType, "Markers");
        _labelParentField = AccessTools.Field(_labelType, "Parent");
        _labelTextField = AccessTools.Field(_labelType, "Label");
        _nameOffsetField = AccessTools.Field(AccessTools.TypeByName("NOX.Plugin"), "NameOffset");
        if (_labelListField != null && _labelParentField != null && _labelTextField != null)
        {
            harmony.Patch(AccessTools.Method(typeof(CombatHUD), "LateUpdate"),
                postfix: new HarmonyMethod(typeof(NoxCompat), nameof(SnapSquadLabels)));
        }
        else
        {
            NOVRLog.Warning("Compat: NOX HUDMarker members not found; squad labels left as is.");
        }
    }

    private static void BindConfig()
    {
        var config = ModConfiguration.Instance?.Config;
        if (config == null) return;
        _moveRwr = config.Bind(ConfigSection, "Move RWR Onto VR HUD", true,
            "Place NOX's RWR display on the VR HUD beside the weapon panel. Disable to leave it where NOX puts it (the head-mounted display's top-right corner, which is usually out of view in VR).");
        _rwrX = config.Bind(ConfigSection, "RWR Position X", 450f,
            "Horizontal offset of the RWR display's centre from the HUD centre, in HUD pixels.");
        _rwrY = config.Bind(ConfigSection, "RWR Position Y", 110f,
            "Vertical offset of the RWR display's centre from the HUD centre, in HUD pixels.");
        _rwrScale = config.Bind(ConfigSection, "RWR Scale", 0.6f,
            new ConfigDescription("Size of the RWR display on the VR HUD; also follows HUD Info Panel Scale.",
                new AcceptableValueRange<float>(0.2f, 2.0f)));
    }

    // NOX looks for TopRightPanel under the HMD when the HMD starts; if NOVR already moved it, NOX's
    // Update dereferences null every frame. Point it at the same panel through CombatHUD instead.
    private static void RwrPrefix(object __instance)
    {
        if (RwrGuard.Failed) return;
        try
        {
            if (_rwrTopRightPanelField!.GetValue(__instance) is RectTransform existing && existing != null) return;
            var hud = SceneSingleton<CombatHUD>.i;
            if (hud == null) return;
            var panel = TopRightPanel(hud);
            if (panel != null && panel.transform is RectTransform rect)
                _rwrTopRightPanelField.SetValue(__instance, rect);
        }
        catch (Exception exception)
        {
            RwrGuard.Fail(exception);
        }
    }

    private static void RwrPostfix(object __instance)
    {
        if (RwrGuard.Failed || _moveRwr is { Value: false }) return;
        try
        {
            if (!CompatHud.TryGetCameras(out _, out _)) return;
            if (_rwrTransformField!.GetValue(__instance) is not RectTransform display || display == null) return;
            var flightHud = SceneSingleton<FlightHud>.i;
            var hudCenter = flightHud != null ? flightHud.GetHUDCenter() : null;
            if (hudCenter == null) return;

            if (display.parent != hudCenter)
            {
                display.SetParent(hudCenter, false);
                display.anchorMin = display.anchorMax = display.pivot = new Vector2(0.5f, 0.5f);
                display.localRotation = Quaternion.identity;
                LayerHelper.SetLayerRecursive(display, LayerHelper.GetVrUiLayer());
            }

            var offset = new Vector3(_rwrX?.Value ?? 450f, _rwrY?.Value ?? 110f, 0f);
            display.localPosition = HudInfoPanelSettings.ApplySpread(offset);
            display.localScale = Vector3.one * ((_rwrScale?.Value ?? 0.6f) * HudInfoPanelSettings.Scale);
        }
        catch (Exception exception)
        {
            RwrGuard.Fail(exception);
        }
    }

    private static void SnapSquadLabels()
    {
        if (LabelGuard.Failed) return;
        try
        {
            if (!CompatHud.TryGetCameras(out _, out var hudCamera)) return;
            if (_labelListField!.GetValue(null) is not IList labels || labels.Count == 0) return;

            var up = hudCamera.transform.up;
            var rotation = hudCamera.transform.rotation;
            var nameOffset = NameOffset();
            foreach (var entry in labels)
            {
                if (entry is not GameObject labelObject || labelObject == null) continue;
                var component = labelObject.GetComponent(_labelType);
                if (component == null) continue;
                if (_labelParentField!.GetValue(component) is not HUDUnitMarker marker || marker == null) continue;
                if (_labelTextField!.GetValue(component) is not Text text || text == null || !text.enabled) continue;
                if (marker.image == null) continue;

                text.transform.SetPositionAndRotation(marker.image.transform.position + up * nameOffset, rotation);
            }
        }
        catch (Exception exception)
        {
            LabelGuard.Fail(exception);
        }
    }

    private static float NameOffset()
    {
        if (_nameOffsetField?.GetValue(null) is ConfigEntryBase entry && entry.BoxedValue is float value)
            return value;
        return 5f;
    }
}
