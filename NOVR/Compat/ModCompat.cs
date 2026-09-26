using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NOVR.Compat;

// Soft-dependency fixes for community mods that draw HUD content. Nothing here references another
// mod at compile time: each entry is looked up by BepInEx plugin GUID and only patched when that
// mod is actually loaded, so with none of them installed this code does nothing.
internal static class ModCompat
{
    private sealed class Entry
    {
        public Entry(string name, string guid, Action<Harmony> apply)
        {
            Name = name;
            Guid = guid;
            Apply = apply;
        }

        public string Name { get; }
        public string Guid { get; }
        public Action<Harmony> Apply { get; }
        public bool Done { get; set; }
    }

    private static readonly List<Entry> Entries = new()
    {
        new Entry("NO Tactitools", NottCompat.PluginGuid, NottCompat.Apply),
    };

    private static Harmony? _harmony;
    private static bool _subscribed;

    internal static void EnsureSubscribed()
    {
        if (_subscribed) return;
        _subscribed = true;
        SceneManager.sceneLoaded += (_, _) => TryApplyAll();
    }

    // Idempotent: called on every scene load and main menu start, applies each entry at most once.
    internal static void TryApplyAll()
    {
        foreach (var entry in Entries)
        {
            if (entry.Done || !IsLoaded(entry.Guid)) continue;
            entry.Done = true;
            try
            {
                _harmony ??= new Harmony("deltawing.novr.compat");
                entry.Apply(_harmony);
                NOVRLog.Info($"Compat: {entry.Name} detected, VR fixes enabled.");
            }
            catch (Exception exception)
            {
                NOVRLog.Error($"Compat: failed to enable VR fixes for {entry.Name}; it will run unmodified.\n{exception}");
            }
        }
    }

    private static bool IsLoaded(string guid)
    {
        try
        {
            return Chainloader.PluginInfos.ContainsKey(guid);
        }
        catch
        {
            return false;
        }
    }
}

// Found by NOVRPlugin's per-class Harmony pass, so no existing file has to call into this folder.
[HarmonyPatch(typeof(MainMenu), "Start")]
internal static class ModCompatBootstrap
{
    private static bool Prepare()
    {
        ModCompat.EnsureSubscribed();
        return true;
    }

    private static void Postfix() => ModCompat.TryApplyAll();
}

// Disables a per-frame compat hook after its first exception, logging it once.
internal sealed class CompatGuard
{
    private readonly string _name;

    public CompatGuard(string name) => _name = name;

    public bool Failed { get; private set; }

    public void Fail(Exception exception)
    {
        if (Failed) return;
        Failed = true;
        NOVRLog.Error($"Compat: {_name} hook failed and is now disabled; the mod keeps its flat-screen behaviour.\n{exception}");
    }
}

internal static class CompatHud
{
    // Main (game) camera and the VR UI camera, or false when NOVR's VR HUD isn't up.
    public static bool TryGetCameras(out Camera mainCamera, out Camera cockpitHudCamera)
    {
        mainCamera = null!;
        cockpitHudCamera = null!;
        if (VrUi.NOUIManager.I == null) return false;
        mainCamera = APIBus.MainCamera;
        cockpitHudCamera = APIBus.CockpitHudCamera;
        return mainCamera != null && cockpitHudCamera != null;
    }

    // VR equivalent of Camera.WorldToScreenPoint + RectTransformUtility.ScreenPointToLocalPointInRectangle
    // for a canvas NOVR renders with the VR UI camera: the game-world direction (relative to the main
    // camera) is re-cast from the VR UI camera and intersected with the canvas plane.
    // depth has WorldToScreenPoint's z meaning (negative = behind the viewer).
    public static Vector2 WorldToCanvasLocal(Vector3 worldPosition, RectTransform canvasRect, Camera mainCamera,
        Camera cockpitHudCamera, out float depth)
    {
        var cameraLocal = mainCamera.transform.InverseTransformPoint(worldPosition);
        depth = cameraLocal.z;

        var direction = cockpitHudCamera.transform.rotation * cameraLocal;
        direction = direction.sqrMagnitude > Mathf.Epsilon ? direction.normalized : cockpitHudCamera.transform.forward;

        var origin = cockpitHudCamera.transform.position;
        var normal = canvasRect.forward;
        var denominator = Vector3.Dot(direction, normal);
        const float minDenominator = 1e-4f;
        if (Mathf.Abs(denominator) < minDenominator)
            denominator = denominator < 0f ? -minDenominator : minDenominator;

        // A negative distance mirrors points behind the viewer, as WorldToScreenPoint does.
        var distance = Vector3.Dot(canvasRect.position - origin, normal) / denominator;
        var local = canvasRect.InverseTransformPoint(origin + direction * distance);
        return new Vector2(local.x, local.y);
    }
}
