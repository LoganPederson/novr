using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace NOVR.VrUi.Hands;

// Reads raw hand joints through the XR_EXT_hand_tracking OpenXR extension (supported by VDXR, Meta Quest Link,
// SteamVR and others). Unity's OpenXR plugin doesn't expose joints without the XR Hands package, so this talks to
// the runtime directly: it resolves the extension functions when the instance is created, makes one hand tracker
// per hand when the session starts, and locates joints on demand from the main thread.
//
// Registered at startup by OpenXrControllerProfileBootstrap.EnsureCustomFeature, before the OpenXR loader starts.
public class OpenXrHandTrackingFeature : OpenXRFeature
{
    public const string featureId = "com.novr.handtracking";
    public const string UiName = "NOVR Hand Tracking";
    public const string HandTrackingExtension = "XR_EXT_hand_tracking";
    // Converts a QueryPerformanceCounter reading to an XrTime, so joints can be located at "now" without hooking the frame loop.
    public const string TimeConversionExtension = "XR_KHR_win32_convert_performance_counter_time";
    public const string ExtensionStrings = HandTrackingExtension + " " + TimeConversionExtension;
    public const int JointCount = 26;

    private const int XrSuccess = 0;
    private const int XrTypeHandTrackerCreateInfo = 1000051001;
    private const int XrTypeHandJointsLocateInfo = 1000051002;
    private const int XrTypeHandJointLocations = 1000051003;
    private const int XrHandLeft = 1;
    private const int XrHandRight = 2;
    private const int XrHandJointSetDefault = 0;
    private const ulong OrientationValid = 0x1;
    private const ulong PositionValid = 0x2;

    public static OpenXrHandTrackingFeature? Instance { get; private set; }

    private ulong _instance;
    private ulong _session;
    private ulong _appSpace;
    private ulong _leftTracker;
    private ulong _rightTracker;
    private IntPtr _leftJointBuffer;
    private IntPtr _rightJointBuffer;
    private bool _loggedLocateFailure;
    private bool _loggedStartFailure;
    private float _nextStartAttempt;
    private const float StartRetryIntervalSeconds = 3f;

    private CreateHandTrackerDelegate? _createHandTracker;
    private DestroyHandTrackerDelegate? _destroyHandTracker;
    private LocateHandJointsDelegate? _locateHandJoints;
    private ConvertPerformanceCounterDelegate? _convertPerformanceCounter;

    // True once both hand trackers exist and joints can be located.
    public bool IsRunning => _leftTracker != 0 && _rightTracker != 0 && _locateHandJoints != null;

    protected override void OnEnable()
    {
        base.OnEnable();
        Instance = this;
    }

    protected override bool OnInstanceCreate(ulong xrInstance)
    {
        // Never fail instance creation: without hand tracking the rest of VR must still start.
        _instance = xrInstance;
        _createHandTracker = null;
        _destroyHandTracker = null;
        _locateHandJoints = null;
        _convertPerformanceCounter = null;

        if (!OpenXRRuntime.IsExtensionEnabled(HandTrackingExtension))
        {
            Log($"The OpenXR runtime ({OpenXRRuntime.name}) does not offer {HandTrackingExtension}; hand tracking is unavailable.");
            return true;
        }

        if (!OpenXRRuntime.IsExtensionEnabled(TimeConversionExtension))
        {
            Log($"The OpenXR runtime ({OpenXRRuntime.name}) does not offer {TimeConversionExtension}; hand tracking is unavailable.");
            return true;
        }

        try
        {
            var getProcAddr = Marshal.GetDelegateForFunctionPointer<GetInstanceProcAddrDelegate>(xrGetInstanceProcAddr);
            _createHandTracker = LoadFunction<CreateHandTrackerDelegate>(getProcAddr, "xrCreateHandTrackerEXT");
            _destroyHandTracker = LoadFunction<DestroyHandTrackerDelegate>(getProcAddr, "xrDestroyHandTrackerEXT");
            _locateHandJoints = LoadFunction<LocateHandJointsDelegate>(getProcAddr, "xrLocateHandJointsEXT");
            _convertPerformanceCounter = LoadFunction<ConvertPerformanceCounterDelegate>(getProcAddr, "xrConvertWin32PerformanceCounterToTimeKHR");
        }
        catch (Exception exception)
        {
            Log($"Failed to load hand tracking functions; hand tracking is unavailable. {exception}");
            _createHandTracker = null;
            _locateHandJoints = null;
        }

        return true;
    }

    protected override void OnSessionCreate(ulong xrSession)
    {
        _session = xrSession;
        _nextStartAttempt = 0f;
        _loggedStartFailure = false;
        // Runtimes such as VDXR can report hand tracking as unsupported (XR_ERROR_FEATURE_UNSUPPORTED) until the
        // headset is actually streaming hand data, so a failure here is retried later by EnsureStarted.
        TryStartTracking();
    }

    // Called every frame from the main thread; retries starting hand tracking while it isn't running.
    public void EnsureStarted()
    {
        if (IsRunning || _session == 0 || _createHandTracker == null) return;
        if (Time.unscaledTime < _nextStartAttempt) return;

        _nextStartAttempt = Time.unscaledTime + StartRetryIntervalSeconds;
        TryStartTracking();
    }

    private void TryStartTracking()
    {
        if (_createHandTracker == null) return;

        var leftResult = CreateTracker(XrHandLeft, out _leftTracker);
        var rightResult = CreateTracker(XrHandRight, out _rightTracker);
        if (_leftTracker != 0 && _rightTracker != 0)
        {
            _leftJointBuffer = Marshal.AllocHGlobal(JointCount * Marshal.SizeOf<XrHandJointLocation>());
            _rightJointBuffer = Marshal.AllocHGlobal(JointCount * Marshal.SizeOf<XrHandJointLocation>());
            Log($"Hand tracking started on {OpenXRRuntime.name}.");
            return;
        }

        DestroyTrackers();
        if (_loggedStartFailure) return;
        _loggedStartFailure = true;
        Log($"xrCreateHandTrackerEXT failed (left XrResult {leftResult}, right XrResult {rightResult}). " +
            $"Retrying every {StartRetryIntervalSeconds:0} seconds. -8 means the runtime says hand tracking isn't available right now; " +
            "check that hand tracking is on in the headset's settings (Virtual Desktop: enable \"Forward tracking data\" in the Streaming tab), and put the controllers down so it switches to hands.");
    }

    protected override void OnAppSpaceChange(ulong xrSpace)
    {
        _appSpace = xrSpace;
    }

    protected override void OnSessionDestroy(ulong xrSession)
    {
        DestroyTrackers();
        _session = 0;
    }

    protected override void OnInstanceDestroy(ulong xrInstance)
    {
        DestroyTrackers();
        _instance = 0;
        _createHandTracker = null;
        _destroyHandTracker = null;
        _locateHandJoints = null;
        _convertPerformanceCounter = null;
    }

    // Locates all joints of one hand at the current time, in Unity's tracking space (the same space as the
    // headset pose from InputTracking). Returns false when the hand isn't tracked right now.
    public unsafe bool TryLocateHand(bool left, HandJointPose[] joints)
    {
        if (!IsRunning || _convertPerformanceCounter == null || joints.Length < JointCount) return false;

        if (!QueryPerformanceCounter(out var counter)) return false;
        if (_convertPerformanceCounter(_instance, ref counter, out var now) != XrSuccess) return false;

        var buffer = left ? _leftJointBuffer : _rightJointBuffer;
        var locateInfo = new XrHandJointsLocateInfo
        {
            Type = XrTypeHandJointsLocateInfo,
            BaseSpace = _appSpace != 0 ? _appSpace : GetCurrentAppSpace(),
            Time = now
        };
        var locations = new XrHandJointLocations
        {
            Type = XrTypeHandJointLocations,
            JointCount = JointCount,
            JointLocations = buffer
        };

        var result = _locateHandJoints!(left ? _leftTracker : _rightTracker, ref locateInfo, ref locations);
        if (result != XrSuccess)
        {
            if (!_loggedLocateFailure)
            {
                _loggedLocateFailure = true;
                Log($"xrLocateHandJointsEXT failed with XrResult {result}; hand input paused until it succeeds.");
            }
            return false;
        }

        if (locations.IsActive == 0) return false;

        var native = (XrHandJointLocation*)buffer;
        for (var i = 0; i < JointCount; i++)
        {
            var joint = native[i];
            var valid = (joint.Flags & (PositionValid | OrientationValid)) == (PositionValid | OrientationValid);
            // OpenXR is right-handed with -Z forward; Unity is left-handed with +Z forward.
            joints[i] = new HandJointPose(
                valid,
                new Vector3(joint.PositionX, joint.PositionY, -joint.PositionZ),
                new Quaternion(-joint.OrientationX, -joint.OrientationY, joint.OrientationZ, joint.OrientationW));
        }

        return true;
    }

    private int CreateTracker(int hand, out ulong tracker)
    {
        var createInfo = new XrHandTrackerCreateInfo
        {
            Type = XrTypeHandTrackerCreateInfo,
            Hand = hand,
            HandJointSet = XrHandJointSetDefault
        };

        var result = _createHandTracker!(_session, ref createInfo, out tracker);
        if (result != XrSuccess) tracker = 0;
        return result;
    }

    private void DestroyTrackers()
    {
        if (_leftTracker != 0) _destroyHandTracker?.Invoke(_leftTracker);
        if (_rightTracker != 0) _destroyHandTracker?.Invoke(_rightTracker);
        _leftTracker = 0;
        _rightTracker = 0;

        if (_leftJointBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_leftJointBuffer);
        if (_rightJointBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_rightJointBuffer);
        _leftJointBuffer = IntPtr.Zero;
        _rightJointBuffer = IntPtr.Zero;
    }

    private T LoadFunction<T>(GetInstanceProcAddrDelegate getProcAddr, string name) where T : Delegate
    {
        var result = getProcAddr(_instance, name, out var function);
        if (result != XrSuccess || function == IntPtr.Zero)
            throw new InvalidOperationException($"xrGetInstanceProcAddr({name}) returned {result}");
        return Marshal.GetDelegateForFunctionPointer<T>(function);
    }

    private static void Log(string message) => Debug.Log($"[NOVR] {message}");

    [DllImport("kernel32.dll")]
    private static extern bool QueryPerformanceCounter(out long performanceCount);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetInstanceProcAddrDelegate(ulong instance, [MarshalAs(UnmanagedType.LPStr)] string name, out IntPtr function);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateHandTrackerDelegate(ulong session, ref XrHandTrackerCreateInfo createInfo, out ulong handTracker);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DestroyHandTrackerDelegate(ulong handTracker);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int LocateHandJointsDelegate(ulong handTracker, ref XrHandJointsLocateInfo locateInfo, ref XrHandJointLocations locations);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ConvertPerformanceCounterDelegate(ulong instance, ref long performanceCounter, out long time);

    // Layouts follow the OpenXR headers for 64-bit Windows.
    [StructLayout(LayoutKind.Sequential)]
    private struct XrHandTrackerCreateInfo
    {
        public int Type;
        public IntPtr Next;
        public int Hand;
        public int HandJointSet;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrHandJointsLocateInfo
    {
        public int Type;
        public IntPtr Next;
        public ulong BaseSpace;
        public long Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrHandJointLocations
    {
        public int Type;
        public IntPtr Next;
        public uint IsActive;
        public uint JointCount;
        public IntPtr JointLocations;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrHandJointLocation
    {
        public ulong Flags;
        public float OrientationX;
        public float OrientationY;
        public float OrientationZ;
        public float OrientationW;
        public float PositionX;
        public float PositionY;
        public float PositionZ;
        public float Radius;
    }
}

public readonly struct HandJointPose
{
    public HandJointPose(bool isValid, Vector3 position, Quaternion rotation)
    {
        IsValid = isValid;
        Position = position;
        Rotation = rotation;
    }

    public bool IsValid { get; }
    public Vector3 Position { get; }
    public Quaternion Rotation { get; }
}

// Joint indices of XR_HAND_JOINT_SET_DEFAULT_EXT.
public static class HandJoint
{
    public const int Palm = 0;
    public const int Wrist = 1;
    public const int ThumbProximal = 3;
    public const int ThumbTip = 5;
    public const int IndexProximal = 7;
    public const int IndexTip = 10;
    public const int MiddleProximal = 12;
}
