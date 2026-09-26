using System;

namespace NOVR.VrUi.Panels;

// Placement of a floating panel on a sphere around the seated head: yaw and pitch in degrees (0/0 is straight
// ahead, positive yaw to the right, positive pitch up) and a distance in meters. Kept free of Unity types so the
// math can be checked outside the game.
internal static class PanelPlacementMath
{
    public const float MinDistance = 0.4f;
    public const float MaxDistance = 8f;
    public const float MinPitch = -80f;
    public const float MaxPitch = 80f;
    public const float MinSize = 0.25f;
    public const float MaxSize = 3f;

    // How much pulling a hand toward you or pushing it away moves a grabbed panel, per meter of hand movement.
    public const float PushPullGain = 4f;
    // Hand movement toward or away from the head below this is ignored, so swinging the arm sideways (which
    // changes its reach a little) doesn't make the panel creep nearer or farther.
    public const float PushPullDeadZone = 0.03f;

    public static void ToDirection(float yawDegrees, float pitchDegrees, out float x, out float y, out float z)
    {
        var yaw = yawDegrees * (Math.PI / 180.0);
        var pitch = pitchDegrees * (Math.PI / 180.0);
        x = (float)(Math.Cos(pitch) * Math.Sin(yaw));
        y = (float)Math.Sin(pitch);
        z = (float)(Math.Cos(pitch) * Math.Cos(yaw));
    }

    public static void FromDirection(float x, float y, float z, out float yawDegrees, out float pitchDegrees)
    {
        var horizontal = Math.Sqrt(x * x + z * z);
        yawDegrees = (float)(Math.Atan2(x, z) * (180.0 / Math.PI));
        pitchDegrees = (float)(Math.Atan2(y, horizontal) * (180.0 / Math.PI));
    }

    // Wraps an angle into [-180, 180).
    public static float WrapDegrees(float degrees)
    {
        var wrapped = (degrees + 180f) % 360f;
        if (wrapped < 0f) wrapped += 360f;
        return wrapped - 180f;
    }

    public static float ClampDistance(float distance) => Clamp(distance, MinDistance, MaxDistance);
    public static float ClampPitch(float pitch) => Clamp(pitch, MinPitch, MaxPitch);
    public static float ClampSize(float size) => Clamp(size, MinSize, MaxSize);

    // Where a grabbed panel goes: it turns by however much the grab point has turned around the head since the grab
    // started, and moves nearer or farther as the hand is pulled in or pushed out.
    public static void Drag(
        float startYaw, float startPitch, float startDistance,
        float grabStartYaw, float grabStartPitch, float grabStartReach,
        float grabYaw, float grabPitch, float grabReach,
        out float yaw, out float pitch, out float distance)
    {
        yaw = WrapDegrees(startYaw + WrapDegrees(grabYaw - grabStartYaw));
        pitch = ClampPitch(startPitch + (grabPitch - grabStartPitch));
        var reachChange = grabReach - grabStartReach;
        reachChange = Math.Abs(reachChange) <= PushPullDeadZone ? 0f : reachChange - Math.Sign(reachChange) * PushPullDeadZone;
        distance = ClampDistance(startDistance + reachChange * PushPullGain);
    }

    private static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
}
