using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using NOVR.VrUi.SpecialBehavior;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NOVR.VrUi.Panels;

// Where one floating panel sits and how big it is, stored in the config so it stays put between sessions.
internal sealed class PanelPlacementSettings
{
    public readonly ConfigEntry<float> Distance;
    public readonly ConfigEntry<float> Yaw;
    public readonly ConfigEntry<float> Pitch;
    public readonly ConfigEntry<float> Size;

    public PanelPlacementSettings(ConfigEntry<float> distance, ConfigEntry<float> yaw, ConfigEntry<float> pitch, ConfigEntry<float> size)
    {
        Distance = distance;
        Yaw = yaw;
        Pitch = pitch;
        Size = size;
    }

    public void Reset()
    {
        Distance.Value = (float)Distance.DefaultValue;
        Yaw.Value = (float)Yaw.DefaultValue;
        Pitch.Value = (float)Pitch.DefaultValue;
        Size.Value = (float)Size.DefaultValue;
    }
}

// A world-space canvas the player can place around them: it sits on a sphere around the seated head, always faces
// it, and gets a small bar under it to move it (press and drag the grip; with a hand, pull in or push out to bring
// it nearer or farther), resize it, reset it and close it. Everything goes through VrUiCursor, so hands,
// controllers and the mouse all work. The canvas's own NOVR placement behaviour is switched off while this
// places it, and switched back on if movable panels are turned off.
[DefaultExecutionOrder(-2000)] // Place the panel before the cursor hit-tests it and before the game reads it.
internal sealed class FloatingPanel : MonoBehaviour
{
    private const float SizeStep = 1.15f;
    // The bar keeps the same apparent size at any distance: meters per bar unit, per meter of distance.
    private const float BarMetersPerUnitPerMeter = 0.0006f;
    private const float BarHeight = 64f;
    private const float BarGap = 12f;
    private const float ButtonHeight = 52f;
    private const float ButtonSpacing = 8f;
    private static readonly Color BarColor = new(0.08f, 0.10f, 0.12f, 0.85f);
    private static readonly Color ButtonColor = new(0.20f, 0.26f, 0.30f, 0.95f);
    private static readonly Color GripColor = new(0.16f, 0.36f, 0.30f, 0.95f);
    private static readonly Color CloseColor = new(0.45f, 0.16f, 0.16f, 0.95f);

    private PanelPlacementSettings? _settings;
    private float _baseScale;
    private Func<RectTransform?>? _barAnchor;
    private Action? _onClose;
    private Func<bool>? _isInUse;
    private readonly List<(string Label, Action OnClick)> _extraButtons = new();

    private Canvas? _canvas;
    private RectTransform? _bar;
    private readonly Vector3[] _corners = new Vector3[4];
    private readonly List<UIRenderedCanvasBehavior> _suspendedBehaviours = new();
    private readonly List<UIRenderedCanvasBehavior> _behaviourBuffer = new();

    private bool _grabbing;
    private float _grabStartYaw, _grabStartPitch, _grabStartDistance;
    private float _grabPointStartYaw, _grabPointStartPitch, _grabStartReach, _grabRayDistance;
    private float _yaw, _pitch, _distance, _size;

    public static readonly List<FloatingPanel> All = new();

    public bool IsShown => isActiveAndEnabled && IsPlacing;
    private bool IsPlacing => _settings != null && IsEnabledInConfig && (_isInUse?.Invoke() ?? true);
    public Canvas? Canvas => _canvas;

    private static bool IsEnabledInConfig => ModConfiguration.Instance?.MovablePanels.Value ?? false;

    // baseScale is the canvas's meters per unit at size 1. barAnchor, if given, is the part of the canvas the bar
    // goes under (the map itself rather than the whole screen-sized map canvas). While isInUse returns false the
    // panel leaves the canvas to its usual behaviour (the map on the spawn screen, where other UI lines up with it).
    public void Configure(PanelPlacementSettings settings, float baseScale, Func<RectTransform?>? barAnchor, Action? onClose, Func<bool>? isInUse = null)
    {
        _settings = settings;
        _baseScale = baseScale;
        _barAnchor = barAnchor;
        _onClose = onClose;
        _isInUse = isInUse;
        ReadSettings();
    }

    public void AddButton(string label, Action onClick)
    {
        _extraButtons.Add((label, onClick));
        if (_bar != null)
        {
            Destroy(_bar.gameObject);
            _bar = null;
        }
    }

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();
    }

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        if (_canvas != null && IsPlacing) VrCanvasHitTester.Register(_canvas);
    }

    private void OnDisable()
    {
        All.Remove(this);
        if (_grabbing) EndGrab();
    }

    private void OnDestroy()
    {
        All.Remove(this);
        ResumeSuspendedBehaviours();
        if (_canvas != null) VrCanvasHitTester.Unregister(_canvas);
    }

    private void Update()
    {
        if (_settings == null || _canvas == null) return;

        if (!IsPlacing)
        {
            ResumeSuspendedBehaviours();
            if (_bar != null) _bar.gameObject.SetActive(false);
            _grabbing = false;
            return;
        }

        SuspendOtherPlacement();
        VrCanvasHitTester.Register(_canvas);
        if (_canvas.worldCamera == null) _canvas.worldCamera = APIBus.CockpitHudCamera;

        if (_grabbing) UpdateGrab();
        else ReadSettings();

        ApplyPlacement();
        UpdateBar();
    }

    public void Grow() => SetSize(_size * SizeStep);
    public void Shrink() => SetSize(_size / SizeStep);

    public void ResetPlacement()
    {
        _grabbing = false;
        _settings?.Reset();
        ReadSettings();
    }

    // Brings the panel to where the head is looking, at the same distance.
    public void BringToView()
    {
        var camera = APIBus.CockpitHudCamera;
        if (_settings == null || camera == null) return;

        var forward = camera.transform.forward;
        PanelPlacementMath.FromDirection(forward.x, forward.y, forward.z, out var yaw, out var pitch);
        _grabbing = false;
        _settings.Yaw.Value = PanelPlacementMath.WrapDegrees(yaw);
        _settings.Pitch.Value = PanelPlacementMath.ClampPitch(pitch);
        ReadSettings();
    }

    public void Close() => _onClose?.Invoke();

    private void SetSize(float size)
    {
        if (_settings == null) return;
        _settings.Size.Value = PanelPlacementMath.ClampSize(size);
        ReadSettings();
    }

    private void ReadSettings()
    {
        if (_settings == null) return;
        _yaw = PanelPlacementMath.WrapDegrees(_settings.Yaw.Value);
        _pitch = PanelPlacementMath.ClampPitch(_settings.Pitch.Value);
        _distance = PanelPlacementMath.ClampDistance(_settings.Distance.Value);
        _size = PanelPlacementMath.ClampSize(_settings.Size.Value);
    }

    private void ApplyPlacement()
    {
        PanelPlacementMath.ToDirection(_yaw, _pitch, out var x, out var y, out var z);
        var direction = new Vector3(x, y, z);
        transform.position = direction * _distance;
        // uGUI world-space canvases read correctly when their forward points away from the viewer.
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        transform.localScale = Vector3.one * (_baseScale * _size);
    }

    // The canvas's usual NOVR behaviour pins it in place every frame; it steps aside while this panel places it.
    // It also registers the canvas with the hit tester, which it undoes when disabled, so this registers it instead.
    private void SuspendOtherPlacement()
    {
        GetComponents(_behaviourBuffer);
        foreach (var behaviour in _behaviourBuffer)
        {
            if (behaviour == null || !behaviour.enabled) continue;
            behaviour.enabled = false;
            if (!_suspendedBehaviours.Contains(behaviour)) _suspendedBehaviours.Add(behaviour);
        }
    }

    private void ResumeSuspendedBehaviours()
    {
        foreach (var behaviour in _suspendedBehaviours)
        {
            if (behaviour != null) behaviour.enabled = true;
        }
        _suspendedBehaviours.Clear();
    }

    private void BeginGrab()
    {
        var cursor = VrUiCursor.I;
        if (cursor == null || _settings == null) return;

        var ray = cursor.PointerRay;
        var grabPoint = cursor.CursorPosition;
        _grabRayDistance = Mathf.Max(0.05f, Vector3.Distance(ray.origin, grabPoint));
        PanelPlacementMath.FromDirection(grabPoint.x, grabPoint.y, grabPoint.z, out _grabPointStartYaw, out _grabPointStartPitch);
        _grabStartReach = GetReach(ray);
        _grabStartYaw = _yaw;
        _grabStartPitch = _pitch;
        _grabStartDistance = _distance;
        _grabbing = true;
    }

    private void UpdateGrab()
    {
        var cursor = VrUiCursor.I;
        if (cursor == null || !cursor.IsPointerDown)
        {
            EndGrab();
            return;
        }

        var ray = cursor.PointerRay;
        var grabPoint = ray.origin + ray.direction.normalized * _grabRayDistance;
        PanelPlacementMath.FromDirection(grabPoint.x, grabPoint.y, grabPoint.z, out var grabYaw, out var grabPitch);
        // A mouse ray starts at the head, so only a hand or controller can push or pull the panel.
        var reach = cursor.IsControllerModeActive ? GetReach(ray) : _grabStartReach;
        PanelPlacementMath.Drag(
            _grabStartYaw, _grabStartPitch, _grabStartDistance,
            _grabPointStartYaw, _grabPointStartPitch, _grabStartReach,
            grabYaw, grabPitch, reach,
            out _yaw, out _pitch, out _distance);
    }

    // How far the pointer (a hand or controller) is held out from the head.
    private static float GetReach(Ray ray)
    {
        var camera = APIBus.CockpitHudCamera;
        return camera != null ? Vector3.Distance(ray.origin, camera.transform.position) : ray.origin.magnitude;
    }

    // Written to the config once on release, not every frame of the drag.
    private void EndGrab()
    {
        _grabbing = false;
        if (_settings == null) return;
        _settings.Yaw.Value = _yaw;
        _settings.Pitch.Value = _pitch;
        _settings.Distance.Value = _distance;
    }

    private void UpdateBar()
    {
        if (_bar == null) _bar = BuildBar();
        if (!_bar.gameObject.activeSelf) _bar.gameObject.SetActive(true);

        var anchor = _barAnchor?.Invoke();
        var bottom = 0f;
        var centerX = 0f;
        if (anchor != null)
        {
            anchor.GetWorldCorners(_corners);
            var bottomLeft = transform.InverseTransformPoint(_corners[0]);
            var bottomRight = transform.InverseTransformPoint(_corners[3]);
            bottom = Mathf.Min(bottomLeft.y, bottomRight.y);
            centerX = (bottomLeft.x + bottomRight.x) * 0.5f;
        }
        else if (transform is RectTransform rect)
        {
            bottom = rect.rect.yMin;
            centerX = rect.rect.center.x;
        }

        var panelScale = Mathf.Max(1e-6f, transform.lossyScale.x);
        var barScale = BarMetersPerUnitPerMeter * _distance / panelScale;
        _bar.localScale = Vector3.one * barScale;
        _bar.localRotation = Quaternion.identity;
        _bar.localPosition = new Vector3(centerX, bottom - (BarGap + BarHeight * 0.5f) * barScale, 0f);
    }

    private RectTransform BuildBar()
    {
        var buttons = new List<(string Label, Color Color, float Width, Action OnClick, bool IsGrip)>
        {
            ("MOVE", GripColor, 150f, () => { }, true),
            ("-", ButtonColor, 64f, Shrink, false),
            ("+", ButtonColor, 64f, Grow, false),
            ("RESET", ButtonColor, 110f, ResetPlacement, false),
        };
        foreach (var (label, onClick) in _extraButtons)
            buttons.Add((label, ButtonColor, 110f, onClick, false));
        if (_onClose != null)
            buttons.Add(("CLOSE", CloseColor, 110f, Close, false));

        var width = ButtonSpacing;
        foreach (var button in buttons) width += button.Width + ButtonSpacing;

        var barObject = new GameObject("NOVR Panel Bar");
        barObject.transform.SetParent(transform, false);
        var bar = barObject.AddComponent<RectTransform>();
        bar.sizeDelta = new Vector2(width, BarHeight);

        // Its own canvas and raycaster, so the bar draws over the panel and the cursor can hit it; registered with
        // the hit tester because the panel's raycaster only sees the panel's own graphics.
        var barCanvas = barObject.AddComponent<Canvas>();
        barCanvas.overrideSorting = true;
        barCanvas.sortingOrder = 7000;
        barObject.AddComponent<GraphicRaycaster>();
        var background = barObject.AddComponent<Image>();
        background.color = BarColor;

        var x = -width * 0.5f + ButtonSpacing;
        foreach (var (label, color, buttonWidth, onClick, isGrip) in buttons)
        {
            var button = PanelUi.CreateButton(label, bar, new Vector2(x + buttonWidth * 0.5f, 0f), new Vector2(buttonWidth, ButtonHeight), color, onClick);
            if (isGrip) button.gameObject.AddComponent<PanelGrip>().Panel = this;
            x += buttonWidth + ButtonSpacing;
        }

        LayerHelper.SetLayerRecursive(barObject.transform, LayerHelper.GetVrUiLayer());
        VrCanvasHitTester.Register(barCanvas);
        return bar;
    }

    // The MOVE button: pressing it starts dragging the panel; the panel follows the pointer until it is released.
    private sealed class PanelGrip : MonoBehaviour, IPointerDownHandler
    {
        public FloatingPanel? Panel;

        public void OnPointerDown(PointerEventData eventData) => Panel?.BeginGrab();
    }
}
