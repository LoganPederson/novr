using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using BepInEx.Configuration;
using UnityEngine;

namespace NOVR;

public class ModConfiguration
{
    public static ModConfiguration Instance;
    

    public readonly ConfigFile Config;
    public readonly ConfigEntry<float> TargetDesignatorOvershoot;
    public readonly ConfigEntry<bool> EnableNativeMenuUi;
    public readonly ConfigEntry<float> NativeMenuScale;
    public readonly ConfigEntry<float> NativeMenuDistance;
    public readonly ConfigEntry<float> NativeMenuHeightOffset;
    public readonly ConfigEntry<float> ZoomSpeed;
    public readonly ConfigEntry<float> MaximumZoom;
    public readonly ConfigEntry<bool> InstantZoomOut;
    public readonly ConfigEntry<string> CursorInputMode;
    public readonly ConfigEntry<bool> EnableHandTracking;
    public readonly ConfigEntry<float> HandRaiseHeight;
    public readonly ConfigEntry<bool> EnableNativeMenuEnvironment;
    public readonly ConfigEntry<bool> EnableExperimentalSteamVrControllerProfiles;
    public readonly ConfigEntry<bool> LogXrStartupDiagnostics;
    public readonly ConfigEntry<bool> VerboseDiagnostics;
    public readonly ConfigEntry<float> CockpitHeadForwardOffset;
    public readonly ConfigEntry<float> CockpitHeadRightOffset;
    public readonly ConfigEntry<KeyCode> RecenterShortcut;
    public readonly ConfigEntry<bool> ShowRecenterInPauseMenu;
    public readonly ConfigEntry<bool> SavePositionTrigger;
    public readonly ConfigEntry<float> MapClickMaxRadius;
    public readonly ConfigEntry<float> HudMinimapOpacity;
    public readonly ConfigEntry<float> HudInfoPanelScale;
    public readonly ConfigEntry<float> HudInfoPanelSpread;
    public readonly ConfigEntry<bool> ShowEnemyTypeOnHover;
    public readonly ConfigEntry<bool> OpenMapFromMinimap;
    public readonly ConfigEntry<bool> MovablePanels;
    public readonly ConfigEntry<float> MapPanelDistance;
    public readonly ConfigEntry<float> MapPanelYaw;
    public readonly ConfigEntry<float> MapPanelPitch;
    public readonly ConfigEntry<float> MapPanelSize;
    public readonly ConfigEntry<float> TargetScreenPanelDistance;
    public readonly ConfigEntry<float> TargetScreenPanelYaw;
    public readonly ConfigEntry<float> TargetScreenPanelPitch;
    public readonly ConfigEntry<float> TargetScreenPanelSize;
    public readonly ConfigEntry<KeyboardShortcut> ToggleMapShortcut;
    public readonly ConfigEntry<KeyboardShortcut> ToggleTargetScreenPanelShortcut;
    public readonly ConfigEntry<KeyboardShortcut> BringPanelsToViewShortcut;
    public readonly ConfigEntry<KeyboardShortcut> GrowPanelShortcut;
    public readonly ConfigEntry<KeyboardShortcut> ShrinkPanelShortcut;

    private readonly Dictionary<string, (ConfigEntry<float> Forward, ConfigEntry<float> Right)> _perPlaneEntries = new();

    public ModConfiguration(ConfigFile config)
    {
        Instance = this;

        Config = config;
        TargetDesignatorOvershoot = config.Bind(
            "General",
            "Target Designator Overshoot",
            1.2f,
            "How much the target designator should multiply rotation to make for easier high off boresight target designation. Set to 1.0 to disable. Values from 1.0 to 2.0 are supported.");

        EnableNativeMenuUi = config.Bind(
            "Experimental",
            "Enable Native Menu UI",
            true,
            "Use NOVR's native VR menu UI for non-flight menus. Disable to fall back to the existing patched game UI.");

        NativeMenuScale = config.Bind(
            "Experimental",
            "Native Menu Scale",
            1.25f,
            "Size multiplier for NOVR's native VR menu UI. Values from 0.75 to 2.0 are supported.");

        NativeMenuDistance = config.Bind(
            "Experimental",
            "Native Menu Distance",
            3.0f,
            "Distance in meters from the headset when NOVR's native VR menu UI is opened or recentered. Values from 1.5 to 6.0 are supported.");

        NativeMenuHeightOffset = config.Bind(
            "Experimental",
            "Native Menu Height Offset",
            0.0f,
            "Vertical offset in meters applied when NOVR's native VR menu UI is opened or recentered. Values from -0.25 to 1.0 are supported.");

        ZoomSpeed = config.Bind(
            "VR Zoom",
            "Zoom Speed",
            2.0f,
            new ConfigDescription(
                "How quickly headset magnification changes while Zoom View is held, in magnification units per second.",
                new AcceptableValueRange<float>(0.1f, 20.0f)));

        MaximumZoom = config.Bind(
            "VR Zoom",
            "Maximum Zoom",
            4.0f,
            new ConfigDescription(
                "Maximum binocular-style headset magnification.",
                new AcceptableValueRange<float>(1.0f, 10.0f)));

        InstantZoomOut = config.Bind(
            "VR Zoom",
            "Instant Zoom Out",
            false,
            "When enabled, any Zoom View out input immediately returns the headset view to 1x magnification.");
        CursorInputMode = config.Bind(
            "Experimental",
            "Cursor Input Mode",
            "Auto",
            "Selects input source for the VR UI cursor. 'Auto' = use controller if tracked, else a raised hand (hand tracking), else mouse. " +
            "'Mouse' = always use mouse. 'Controller' = always use controller ray. 'Hands' = always use hand tracking.");

        EnableHandTracking = config.Bind(
            "Input",
            "Hand Tracking",
            true,
            "Point at menus with your hand and pinch thumb and index finger to click, without holding controllers. " +
            "Needs a runtime with XR_EXT_hand_tracking (e.g. Virtual Desktop's VDXR, Meta Quest Link) and hand tracking enabled on the headset. " +
            "Only a raised hand points, so hands resting on a HOTAS or in your lap never click. Takes effect after restarting the game.");

        HandRaiseHeight = config.Bind(
            "Input",
            "Hand Raise Height",
            0.4f,
            new ConfigDescription(
                "How far below eye level, in meters, a hand must be raised before it starts pointing. " +
                "Lower it if your hands on the stick and throttle are being picked up; raise it if pointing feels hard to start.",
                new AcceptableValueRange<float>(0.15f, 0.8f)));

        EnableNativeMenuEnvironment = config.Bind(
            "Experimental",
            "Enable Native Menu Environment",
            false,
            "Show an experimental 3D native menu environment using real game preview assets.");

        EnableExperimentalSteamVrControllerProfiles = config.Bind(
            "Experimental",
            "Enable Experimental SteamVR Controller Profiles",
            false,
            "Register a minimal set of OpenXR controller interaction profiles before VR startup. Enables Valve Index, HTC Vive, and Khronos Simple Controller profiles only; hand tracking is not enabled.");

        LogXrStartupDiagnostics = config.Bind(
            "Diagnostics",
            "Log XR Startup Diagnostics",
            false,
            "Log read-only XR loader, OpenXR runtime, subsystem, and input device state during VR startup.");

        VerboseDiagnostics = config.Bind(
            "Diagnostics",
            "Verbose Diagnostics",
            false,
            "When enabled, NOVR emits per-frame and per-second diagnostic logs " +
            "(controller laser dumps, controller input pose dumps, cursor mode dumps, " +
            "asset-cache waiting messages). Disabled by default for performance — " +
            "enable only when troubleshooting.");

        CockpitHeadForwardOffset = config.Bind(
            "Experimental",
            "Cockpit Head Forward Offset",
            0.08f,
            "Offset in meters applied to the cockpit head forward vector. Helps keep the ejection seat bars out of your face.");

        CockpitHeadRightOffset = config.Bind(
            "Experimental",
            "Cockpit Head Right Offset",
            0.0f,
            "Offset in meters applied to the cockpit head right vector. Moves you left (negative) or right (positive) to correct off-center seating.");

        RecenterShortcut = config.Bind(
            "Input",
            "Recenter Shortcut",
            KeyCode.F9,
            "Keyboard shortcut to recenter the VR view. For HOTAS users, map a joystick button to this key via external software.");

        ShowRecenterInPauseMenu = config.Bind(
            "Input",
            "Show Recenter In Pause Menu",
            true,
            "Add a 'RECENTER VIEW' button to the in-game pause menu while seated in a cockpit. Clicking it recenters the VR view immediately.");

        SavePositionTrigger = config.Bind(
            "Experimental",
            "Save Position For Current Aircraft",
            false,
            "Check this box to save the current Cockpit Head Forward/Right Offset values for the aircraft you're currently in. Automatically unchecks itself.");

        MapClickMaxRadius = config.Bind(
            "Map",
            "Click Max Radius",
            0.0375f,
            "Maximum normalized distance from the VR cursor to a map icon for the icon to be selectable on click. Distance is measured as a fraction of the map image's smaller dimension, so it stays consistent regardless of zoom level, HUD scale, or HMD resolution. Values from 0.0 to 0.5 are supported.");

        HudMinimapOpacity = config.Bind(
            "HUD",
            "Minimap Opacity",
            1.0f,
            "Opacity of the in-cockpit minimap (the small map in the HUD, not the full clickable map). " +
            "1.0 is fully opaque, 0.0 hides it completely. Applies only when the minimap is shown; the full map view is unaffected.");

        HudInfoPanelScale = config.Bind(
            "HUD",
            "Info Panel Scale",
            1.0f,
            new ConfigDescription(
                "Size of the HUD's fixed info panels: weapons and countermeasures, the lower-left panel, the speed/altitude/heading gauges " +
                "and the status display. Markers drawn over the world (flight path, pitch ladder, target boxes) are not affected, " +
                "so they stay lined up with what they point at.",
                new AcceptableValueRange<float>(0.5f, 2.0f)));

        ShowEnemyTypeOnHover = config.Bind(
            "HUD",
            "Show Enemy Type On Hover",
            true,
            "When looking at a hostile unit's marker, label it with its type (the same name the target screen shows once it's locked). " +
            "The base game only labels friendly aircraft; turn this off to match it, e.g. for multiplayer servers that expect vanilla information.");

        HudInfoPanelSpread = config.Bind(
            "HUD",
            "Info Panel Spread",
            1.0f,
            new ConfigDescription(
                "How far the same info panels sit from the center of the HUD. Lower values pull them toward the center, " +
                "which helps on headsets with a narrower field of view.",
                new AcceptableValueRange<float>(0.5f, 1.5f)));

        OpenMapFromMinimap = config.Bind(
            "Map",
            "Open Map From Minimap",
            true,
            "In flight, clicking the small map on the HUD (point a raised hand at it and pinch, or use a controller) opens the full, clickable map. " +
            "Close it again with the game's map key or the CLOSE button under the map.");

        MovablePanels = config.Bind(
            "Panels",
            "Movable Panels",
            true,
            "In flight, show a bar under the full map (and the target screen panel) to move, resize, reset and close it. " +
            "Press and drag MOVE to move a panel around you; with a hand, also pull it in or push it away. " +
            "Where you leave a panel is saved below. Turn off to keep the map where the game puts it.");

        MapPanelDistance = config.Bind(
            "Panels",
            "Map Panel Distance",
            3.0f,
            new ConfigDescription("Distance in meters from your head to the full map in flight.",
                new AcceptableValueRange<float>(0.4f, 8.0f)));

        MapPanelYaw = config.Bind(
            "Panels",
            "Map Panel Yaw",
            0.0f,
            new ConfigDescription("Direction of the full map in flight, in degrees to the right of straight ahead (negative is left).",
                new AcceptableValueRange<float>(-180.0f, 180.0f)));

        MapPanelPitch = config.Bind(
            "Panels",
            "Map Panel Pitch",
            0.0f,
            new ConfigDescription("Height of the full map in flight, in degrees above straight ahead (negative is below).",
                new AcceptableValueRange<float>(-80.0f, 80.0f)));

        MapPanelSize = config.Bind(
            "Panels",
            "Map Panel Size",
            1.0f,
            new ConfigDescription("Size of the full map in flight. 1.0 is the size the map has at 3 m by default.",
                new AcceptableValueRange<float>(0.25f, 3.0f)));

        TargetScreenPanelDistance = config.Bind(
            "Panels",
            "Target Screen Panel Distance",
            1.0f,
            new ConfigDescription("Distance in meters from your head to the floating copy of the cockpit's target screen.",
                new AcceptableValueRange<float>(0.4f, 8.0f)));

        TargetScreenPanelYaw = config.Bind(
            "Panels",
            "Target Screen Panel Yaw",
            25.0f,
            new ConfigDescription("Direction of the target screen panel, in degrees to the right of straight ahead (negative is left).",
                new AcceptableValueRange<float>(-180.0f, 180.0f)));

        TargetScreenPanelPitch = config.Bind(
            "Panels",
            "Target Screen Panel Pitch",
            -20.0f,
            new ConfigDescription("Height of the target screen panel, in degrees above straight ahead (negative is below).",
                new AcceptableValueRange<float>(-80.0f, 80.0f)));

        TargetScreenPanelSize = config.Bind(
            "Panels",
            "Target Screen Panel Size",
            1.0f,
            new ConfigDescription("Size of the target screen panel. 1.0 is about 40 cm across.",
                new AcceptableValueRange<float>(0.25f, 3.0f)));

        ToggleMapShortcut = config.Bind(
            "Panels",
            "Toggle Map Shortcut",
            KeyboardShortcut.Empty,
            "Key that opens or closes the full map, like the game's own map key. " +
            "Unset by default; HOTAS users can map a stick button to a key with their joystick software, or use a joystick button directly (e.g. JoystickButton5).");

        ToggleTargetScreenPanelShortcut = config.Bind(
            "Panels",
            "Toggle Target Screen Panel Shortcut",
            KeyboardShortcut.Empty,
            "Key that shows or hides a floating copy of the cockpit's target screen. The TGT SCREEN button under the full map does the same.");

        BringPanelsToViewShortcut = config.Bind(
            "Panels",
            "Bring Panels To View Shortcut",
            KeyboardShortcut.Empty,
            "Key that moves the open panels to where you are looking, keeping their distance.");

        GrowPanelShortcut = config.Bind(
            "Panels",
            "Grow Panel Shortcut",
            KeyboardShortcut.Empty,
            "Key that makes a panel bigger: the one under the cursor, else the full map, else the target screen panel.");

        ShrinkPanelShortcut = config.Bind(
            "Panels",
            "Shrink Panel Shortcut",
            KeyboardShortcut.Empty,
            "Key that makes a panel smaller: the one under the cursor, else the full map, else the target screen panel.");

        SavePositionTrigger.SettingChanged += (_, _) =>
        {
            if (!SavePositionTrigger.Value) return;
            SavePositionTrigger.Value = false;

            if (string.IsNullOrEmpty(Core.CurrentAircraftId))
            {
                Debug.LogWarning("[NOVR] Cannot save cockpit offset: no aircraft is currently active.");
                return;
            }

            SaveCurrentOffsetFor(Core.CurrentAircraftId, CockpitHeadForwardOffset.Value, CockpitHeadRightOffset.Value);
        };

        PreloadPerPlaneEntries();
    }

    private void PreloadPerPlaneEntries()
    {
        if (!File.Exists(Config.ConfigFilePath)) return;

        var inPerPlaneSection = false;
        foreach (var rawLine in File.ReadAllLines(Config.ConfigFilePath))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                inPerPlaneSection = line.Equals("[PerPlaneOffsets]");
                continue;
            }

            if (!inPerPlaneSection) continue;

            var equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0) continue;

            var key = line.Substring(0, equalsIndex).Trim();
            if (key.EndsWith("_Forward"))
            {
                GetOrCreatePlaneEntries(key.Substring(0, key.Length - "_Forward".Length));
            }
            else if (key.EndsWith("_Right"))
            {
                GetOrCreatePlaneEntries(key.Substring(0, key.Length - "_Right".Length));
            }
        }
    }

    private (ConfigEntry<float> Forward, ConfigEntry<float> Right) GetOrCreatePlaneEntries(string aircraftId)
    {
        if (_perPlaneEntries.TryGetValue(aircraftId, out var entries)) return entries;

        var forward = Config.Bind(
            "PerPlaneOffsets",
            $"{aircraftId}_Forward",
            float.NaN,
            "Saved Cockpit Head Forward Offset for this aircraft type. NaN means no value has been saved yet.");

        var right = Config.Bind(
            "PerPlaneOffsets",
            $"{aircraftId}_Right",
            float.NaN,
            "Saved Cockpit Head Right Offset for this aircraft type. NaN means no value has been saved yet.");

        entries = (forward, right);
        _perPlaneEntries[aircraftId] = entries;
        return entries;
    }

    public bool TryGetSavedOffset(string aircraftId, out float forward, out float right)
    {
        forward = 0f;
        right = 0f;
        if (string.IsNullOrEmpty(aircraftId)) return false;

        var entries = GetOrCreatePlaneEntries(aircraftId);
        if (float.IsNaN(entries.Forward.Value) || float.IsNaN(entries.Right.Value)) return false;

        forward = entries.Forward.Value;
        right = entries.Right.Value;
        return true;
    }

    public void SaveCurrentOffsetFor(string aircraftId, float forward, float right)
    {
        if (string.IsNullOrEmpty(aircraftId)) return;

        var entries = GetOrCreatePlaneEntries(aircraftId);
        entries.Forward.Value = forward;
        entries.Right.Value = right;
    }
}
