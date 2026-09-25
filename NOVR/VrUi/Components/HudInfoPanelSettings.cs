using UnityEngine;

namespace NOVR.VrUi.SpecialBehavior;

// Shared reads of the HUD info panel settings, clamped to the ranges the config advertises.
internal static class HudInfoPanelSettings
{
    public static float Scale => Mathf.Clamp(ModConfiguration.Instance?.HudInfoPanelScale.Value ?? 1.0f, 0.5f, 2.0f);

    public static float Spread => Mathf.Clamp(ModConfiguration.Instance?.HudInfoPanelSpread.Value ?? 1.0f, 0.5f, 1.5f);

    // Moves a panel's offset from the HUD center by the spread setting, leaving depth alone.
    public static Vector3 ApplySpread(Vector3 offset) => new(offset.x * Spread, offset.y * Spread, offset.z);
}
