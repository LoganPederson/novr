using UnityEngine;
using UnityEngine.XR;

namespace NOVR.VrUi.Hands;

// Turns tracked hand joints into a UI pointer: a ray and a pinch "trigger", like a controller's.
//
// Aiming: the ray runs from an estimated shoulder position through the hand's pinch point (between the index
// and thumb knuckles). That is the approach headset makers use for hand rays, because it barely moves when the
// fingers close to pinch, unlike pointing with the finger itself.
//
// Safety for HOTAS pilots: a hand only points while it is raised toward eye level and held in front of you
// (Hand Raise Height), so hands on the stick and throttle never drive the cursor. A pinch only clicks if the
// fingers were seen open after the hand was raised, so a grip that looks like a pinch can't click on the way up.
internal static class HandPointer
{
    private const float PinchPressDistance = 0.02f;
    private const float PinchReleaseDistance = 0.035f;
    private const float RaiseHysteresis = 0.05f;
    private const float MinimumForwardReach = 0.1f;
    private const float ShoulderDrop = 0.15f;
    private const float ShoulderHalfWidth = 0.17f;

    private sealed class HandState
    {
        public readonly bool IsLeft;
        public readonly HandJointPose[] Joints = new HandJointPose[OpenXrHandTrackingFeature.JointCount];
        public readonly OneEuroVector3Filter OriginFilter = new(1.0f, 0.4f);
        public readonly OneEuroQuaternionFilter RotationFilter = new(0.6f, 0.3f);
        public bool Tracked;
        public bool Raised;
        public bool OpenSinceRaised;
        public bool Pinching;
        public bool PinchStartedThisFrame;
        public bool FiltersPrimed;
        public Vector3 Origin;
        public Quaternion Rotation;

        public HandState(bool isLeft) => IsLeft = isLeft;
    }

    private static readonly HandState Left = new(true);
    private static readonly HandState Right = new(false);
    private static HandState? _activeHand;
    private static int _updatedFrame = -1;

    // True while a raised hand is pointing. Origin and rotation are in the same space as the controller ray.
    public static bool TryGetPointer(out Vector3 origin, out Quaternion rotation, out bool pinching, out bool pinchStartedThisFrame)
    {
        UpdateIfNeeded();
        if (_activeHand == null)
        {
            origin = Vector3.zero;
            rotation = Quaternion.identity;
            pinching = false;
            pinchStartedThisFrame = false;
            return false;
        }

        origin = _activeHand.Origin;
        rotation = _activeHand.Rotation;
        pinching = _activeHand.Pinching;
        pinchStartedThisFrame = _activeHand.PinchStartedThisFrame;
        return true;
    }

    private static void UpdateIfNeeded()
    {
        if (_updatedFrame == Time.frameCount) return;
        _updatedFrame = Time.frameCount;

        var feature = OpenXrHandTrackingFeature.Instance;
        var enabled = ModConfiguration.Instance?.EnableHandTracking.Value ?? false;
        if (!enabled || feature == null || !feature.IsRunning)
        {
            Reset(Left);
            Reset(Right);
            _activeHand = null;
            return;
        }

        var headPosition = InputTracking.GetLocalPosition(XRNode.Head);
        var headRotation = InputTracking.GetLocalRotation(XRNode.Head);
        UpdateHand(Left, feature, headPosition, headRotation);
        UpdateHand(Right, feature, headPosition, headRotation);
        _activeHand = ChooseActiveHand();
    }

    private static void UpdateHand(HandState hand, OpenXrHandTrackingFeature feature, Vector3 headPosition, Quaternion headRotation)
    {
        hand.PinchStartedThisFrame = false;
        hand.Tracked = feature.TryLocateHand(hand.IsLeft, hand.Joints) &&
                       hand.Joints[HandJoint.IndexProximal].IsValid &&
                       hand.Joints[HandJoint.ThumbProximal].IsValid &&
                       hand.Joints[HandJoint.IndexTip].IsValid &&
                       hand.Joints[HandJoint.ThumbTip].IsValid;
        if (!hand.Tracked)
        {
            Reset(hand);
            return;
        }

        var indexKnuckle = hand.Joints[HandJoint.IndexProximal].Position;
        var thumbKnuckle = hand.Joints[HandJoint.ThumbProximal].Position;
        var pinchPoint = Vector3.Lerp(indexKnuckle, thumbKnuckle, 0.5f);

        // Yaw-only head frame, so looking up or down doesn't move the shoulders.
        var headForward = Vector3.ProjectOnPlane(headRotation * Vector3.forward, Vector3.up);
        if (headForward.sqrMagnitude < 1e-4f) headForward = Vector3.forward;
        headForward.Normalize();
        var headRight = Vector3.Cross(Vector3.up, headForward);

        // Raised and in front of the face, with a little hysteresis so the pointer doesn't flicker at the edge.
        var raiseHeight = Mathf.Clamp(ModConfiguration.Instance?.HandRaiseHeight.Value ?? 0.4f, 0.15f, 0.8f);
        var threshold = headPosition.y - raiseHeight - (hand.Raised ? RaiseHysteresis : 0f);
        var reach = Vector3.Dot(pinchPoint - headPosition, headForward);
        var raised = pinchPoint.y >= threshold && reach >= MinimumForwardReach;
        if (raised && !hand.Raised) hand.OpenSinceRaised = false;
        hand.Raised = raised;
        if (!raised)
        {
            hand.Pinching = false;
            hand.FiltersPrimed = false;
            return;
        }

        var pinchDistance = Vector3.Distance(hand.Joints[HandJoint.ThumbTip].Position, hand.Joints[HandJoint.IndexTip].Position);
        if (pinchDistance > PinchReleaseDistance) hand.OpenSinceRaised = true;
        if (hand.Pinching)
        {
            if (pinchDistance > PinchReleaseDistance) hand.Pinching = false;
        }
        else if (hand.OpenSinceRaised && pinchDistance < PinchPressDistance)
        {
            hand.Pinching = true;
            hand.PinchStartedThisFrame = true;
        }

        var shoulder = headPosition + Vector3.down * ShoulderDrop + headRight * (hand.IsLeft ? -ShoulderHalfWidth : ShoulderHalfWidth);
        var direction = pinchPoint - shoulder;
        if (direction.sqrMagnitude < 1e-4f) direction = headForward;

        // Same conversion from tracking space to the UI's space as the headset camera: position relative to the
        // calibrated head, turned by the recenter rotation.
        var calibration = NOVRHeadsetData.RotationCalibrationOffset;
        var origin = NOVRHeadsetData.Translation + calibration * (pinchPoint - headPosition);
        var rotation = calibration * Quaternion.LookRotation(direction.normalized, Vector3.up);

        if (!hand.FiltersPrimed)
        {
            hand.OriginFilter.Reset(origin);
            hand.RotationFilter.Reset(rotation);
            hand.FiltersPrimed = true;
        }

        var dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
        hand.Origin = hand.OriginFilter.Filter(origin, dt);
        hand.Rotation = hand.RotationFilter.Filter(rotation, dt);
    }

    // Prefer the hand that is pinching, then the hand already in use, then the right hand.
    private static HandState? ChooseActiveHand()
    {
        var leftReady = Left.Tracked && Left.Raised;
        var rightReady = Right.Tracked && Right.Raised;
        if (leftReady && rightReady)
        {
            if (Left.Pinching != Right.Pinching) return Left.Pinching ? Left : Right;
            return _activeHand ?? Right;
        }

        if (rightReady) return Right;
        return leftReady ? Left : null;
    }

    private static void Reset(HandState hand)
    {
        hand.Tracked = false;
        hand.Raised = false;
        hand.OpenSinceRaised = false;
        hand.Pinching = false;
        hand.PinchStartedThisFrame = false;
        hand.FiltersPrimed = false;
    }
}
