using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace NOVR.Compat;

// NO Tactitools (NOTT, https://github.com/clumzy/NO_Tactitools, MIT).
//
// Artificial horizon (UI/HMD/ArtificialHorizon.cs, LogicEngine.Update): projects horizon and cardinal
// points with mainCamera.WorldToScreenPoint and maps them onto CombatHUD with
// ScreenPointToLocalPointInRectangle(rect, point, null, ...). Under NOVR CombatHUD is a world-space
// canvas drawn by the VR UI camera, so the flat-screen maths puts the lines in the wrong place.
// Those two calls (only in that method) are swapped for a VR projection onto the same canvas.
//
// Unit distance (UI/HMD/UnitDistance.cs): a HUDUnitMarker.UpdatePosition postfix writes a world
// rotation of Euler(0,0,0|180) to enemy aircraft markers, and only when the near/far state changes.
// NOVR re-billboards every marker to the VR UI camera each frame, so the flip is lost after one frame
// and untracked markers lose their billboard. A later postfix reapplies NOTT's flip on top of NOVR's
// billboard rotation.
internal static class NottCompat
{
    internal const string PluginGuid = "com.george.NO_Tactitools";

    private const string HorizonLogicType = "NO_Tactitools.UI.HMD.ArtificialHorizonComponent+LogicEngine";
    private const string UnitDistanceTaskType = "NO_Tactitools.UI.HMD.UnitDistanceTask";
    private const string UnitDistancePluginType = "NO_Tactitools.UI.HMD.UnitDistancePlugin";
    // Width of NOTT's near/far transition band, in metres (UnitDistance.cs).
    private const float TransitionBand = 250f;

    private static readonly CompatGuard HorizonGuard = new("NOTT artificial horizon");
    private static readonly CompatGuard MarkerGuard = new("NOTT unit distance");
    private static readonly AccessTools.FieldRef<HUDUnitMarker, Transform> MarkerTransform =
        AccessTools.FieldRefAccess<HUDUnitMarker, Transform>("_transform");

    private static FieldInfo? _unitStatesField;
    private static FieldInfo? _thresholdField;

    internal static void Apply(Harmony harmony)
    {
        var horizonUpdate = AccessTools.Method(AccessTools.TypeByName(HorizonLogicType), "Update");
        if (horizonUpdate != null)
            harmony.Patch(horizonUpdate, transpiler: new HarmonyMethod(typeof(NottCompat), nameof(HorizonTranspiler)));
        else
            NOVRLog.Warning($"Compat: NOTT {HorizonLogicType}.Update not found; artificial horizon left as is.");

        _unitStatesField = AccessTools.Field(AccessTools.TypeByName(UnitDistanceTaskType), "unitStates");
        _thresholdField = AccessTools.Field(AccessTools.TypeByName(UnitDistancePluginType), "unitDistanceThreshold");
        if (_unitStatesField != null && _thresholdField != null)
        {
            harmony.Patch(AccessTools.Method(typeof(HUDUnitMarker), nameof(HUDUnitMarker.UpdatePosition)),
                postfix: new HarmonyMethod(typeof(NottCompat), nameof(UnitMarkerPostfix)) { priority = Priority.Last });
        }
        else
        {
            NOVRLog.Warning("Compat: NOTT unit distance state not found; marker flip left as is.");
        }
    }

    private static IEnumerable<CodeInstruction> HorizonTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var worldToScreen = AccessTools.Method(typeof(Camera), nameof(Camera.WorldToScreenPoint), new[] { typeof(Vector3) });
        var screenToLocal = AccessTools.Method(typeof(RectTransformUtility),
            nameof(RectTransformUtility.ScreenPointToLocalPointInRectangle),
            new[] { typeof(RectTransform), typeof(Vector2), typeof(Camera), typeof(Vector2).MakeByRefType() });
        var vrWorldToScreen = AccessTools.Method(typeof(NottCompat), nameof(WorldToHudPoint));
        var vrScreenToLocal = AccessTools.Method(typeof(NottCompat), nameof(HudPointToLocal));

        var replaced = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(worldToScreen))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = vrWorldToScreen;
                replaced++;
            }
            else if (instruction.Calls(screenToLocal))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = vrScreenToLocal;
                replaced++;
            }

            yield return instruction;
        }

        NOVRLog.Info($"Compat: NOTT artificial horizon projection redirected ({replaced} calls).");
    }

    // Both replacements make the same decision, so a WorldToHudPoint result is always consumed by the
    // matching HudPointToLocal mode within one LogicEngine.Update.
    private static bool TryGetVrHud(out Camera mainCamera, out Camera hudCamera, out RectTransform combatHud)
    {
        combatHud = null!;
        mainCamera = null!;
        hudCamera = null!;
        if (HorizonGuard.Failed) return false;
        try
        {
            var hud = SceneSingleton<CombatHUD>.i;
            if (hud == null || !CompatHud.TryGetCameras(out mainCamera, out hudCamera)) return false;
            combatHud = (hud.transform as RectTransform)!;
            return combatHud != null;
        }
        catch (Exception exception)
        {
            HorizonGuard.Fail(exception);
            return false;
        }
    }

    // Replaces camera.WorldToScreenPoint(world): returns CombatHUD-local x/y plus the usual depth.
    public static Vector3 WorldToHudPoint(Camera camera, Vector3 worldPosition)
    {
        if (TryGetVrHud(out var mainCamera, out var hudCamera, out var combatHud))
        {
            try
            {
                var local = CompatHud.WorldToCanvasLocal(worldPosition, combatHud, mainCamera, hudCamera, out var depth);
                return new Vector3(local.x, local.y, depth);
            }
            catch (Exception exception)
            {
                HorizonGuard.Fail(exception);
            }
        }

        return camera.WorldToScreenPoint(worldPosition);
    }

    // Replaces ScreenPointToLocalPointInRectangle: the point is already canvas-local in VR.
    public static bool HudPointToLocal(RectTransform rect, Vector2 screenPoint, Camera camera, out Vector2 localPoint)
    {
        if (TryGetVrHud(out _, out _, out _))
        {
            localPoint = screenPoint;
            return true;
        }

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPoint, camera, out localPoint);
    }

    private static void UnitMarkerPostfix(HUDUnitMarker __instance)
    {
        if (MarkerGuard.Failed) return;
        try
        {
            if (!CompatHud.TryGetCameras(out _, out var hudCamera)) return;
            if (__instance.unit is not Aircraft) return;
            if (_unitStatesField!.GetValue(null) is not IDictionary states) return;

            var markerTransform = MarkerTransform(__instance);
            if (markerTransform == null) return;

            var state = states.Contains(__instance) ? states[__instance] as string : null;
            var roll = state switch
            {
                "near" => 180f,
                "transition" => TransitionRoll(__instance),
                _ => 0f,
            };
            markerTransform.rotation = hudCamera.transform.rotation * Quaternion.Euler(0f, 0f, roll);
        }
        catch (Exception exception)
        {
            MarkerGuard.Fail(exception);
        }
    }

    private static float TransitionRoll(HUDUnitMarker marker)
    {
        var player = SceneSingleton<CombatHUD>.i?.aircraft;
        if (player == null || marker.unit == null) return 0f;
        var threshold = Convert.ToSingle(_thresholdField!.GetValue(null));
        var distance = Vector3.Distance(marker.unit.transform.position, player.transform.position);
        return Mathf.Lerp(180f, 0f, (distance - threshold) / TransitionBand);
    }
}
