using System.Collections.Generic;
using NOVR.VrUi.HarmonyPatches;
using UnityEngine;

namespace NOVR.VrUi.SpecialBehavior;

public class NOVRHMDBehavior : UIRenderedCanvasBehavior
{
    // Children are looked up by name once and cached; the recursive search used to run four times a frame.
    private readonly Dictionary<string, Transform> _childCache = new();
    private readonly Dictionary<Transform, Vector3> _baseScales = new();

    private void Update()
    {
        var uiCam = APIBus.CockpitHudReference;
        transform.position = uiCam.transform.forward * VrHudProjectionHelper.HudDistance;
        transform.rotation = uiCam.transform.rotation;

        SetGauge("Speed", new Vector3(-110f, 150f, 0f)); // TODO: Patch game files and use events to set these gameobjects
        SetGauge("Altitude", new Vector3(110f, 150f, 0f));
        SetGauge("Bearing", new Vector3(0f, 200f, 0f));
        SetGauge("Artificial Horizon", new Vector3(0f, 150f, 0f));
    }

    // Places a gauge at its offset from the view center, adjusted by the HUD info panel settings.
    private void SetGauge(string childName, Vector3 localPosition)
    {
        var child = GetChild(childName);
        if (child == null)
        {
            return;
        }

        if (!_baseScales.TryGetValue(child, out var baseScale))
        {
            baseScale = child.localScale;
            _baseScales[child] = baseScale;
        }

        child.localPosition = HudInfoPanelSettings.ApplySpread(localPosition);
        child.localScale = baseScale * HudInfoPanelSettings.Scale;
    }

    private void SetLocalPositionRotationAndScale(
        string childName,
        Vector3 localPosition,
        Vector3 localEulerAngles,
        Vector3 localScale)
    {
        var child = GetChild(childName);
        if (child == null)
        {
            return;
        }

        child.localPosition = localPosition;
        child.localEulerAngles = localEulerAngles;
        child.localScale = localScale;
    }

    private Transform? GetChild(string childName)
    {
        if (_childCache.TryGetValue(childName, out var cached) && cached != null)
        {
            return cached;
        }

        var child = FindChildRecursive(transform, childName);
        if (child != null)
        {
            _childCache[childName] = child;
        }

        return child;
    }

    private static Transform? FindChildRecursive(Transform parent, string childName)
    {
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name == childName)
            {
                return child;
            }

            var nestedChild = FindChildRecursive(child, childName);
            if (nestedChild != null)
            {
                return nestedChild;
            }
        }

        return null;
    }
}
