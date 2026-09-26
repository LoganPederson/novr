using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NOVR.VrUi.SpecialBehavior;

public class NOVRDynamicMapBehavior : MonoBehaviour
{
    private static readonly AccessTools.FieldRef<global::DynamicMap, Vector2> PositionOffsetField =
        AccessTools.FieldRefAccess<global::DynamicMap, Vector2>("positionOffset");
    private static readonly System.Reflection.FieldInfo? MapChangedField =
        AccessTools.Field(typeof(global::DynamicMap), "onMapChanged");

    // How far, as a fraction of the map's width, a pinch or trigger press has to move before it pans the map,
    // so pressing an icon to select it doesn't also nudge the map.
    private const float PanStartFraction = 0.02f;

    private Canvas? _canvas;
    private global::DynamicMap? _map;
    private bool _wasPointerDown;
    private bool _panCandidate;
    private bool _panning;
    private Vector2 _panStartLocal;
    private Vector2 _panLastLocal;

    private void Start()
    {
        _canvas = GetComponentInParent<Canvas>();
        if (_canvas != null)
        {
            _canvas.worldCamera = APIBus.CockpitHudCamera;
            VrCanvasHitTester.Register(_canvas);
        }

        // The game uses coordinate-math for map interaction instead of EventSystem,
        // so map graphics have raycastTarget=false by design. The VR cursor's
        // HasGraphicAtPoint check needs raycastTarget to find the map surface.
        var map = GetComponent<global::DynamicMap>();
        _map = map;
        if (map != null)
        {
            if (map.mapBackground != null)
            {
                var bgImg = map.mapBackground.GetComponent<Image>();
                if (bgImg != null)
                {
                    bgImg.raycastTarget = true;
                    VrCanvasHitTester.RegisterSurfaceGraphic(bgImg);
                }
            }
            if (map.mapImage != null)
            {
                var mapImg = map.mapImage.GetComponent<Image>();
                if (mapImg != null)
                {
                    mapImg.raycastTarget = true;
                    VrCanvasHitTester.RegisterSurfaceGraphic(mapImg);
                }
            }
        }
    }

    private void Update()
    {
        UpdateRayPan();
    }

    // The game pans the full map by holding the left mouse button and moving the mouse. A hand or controller has
    // no mouse movement to give it, so pressing on the map and moving the ray drags the map along with the cursor.
    // Mouse mode is left to the game so the two don't pan twice.
    private void UpdateRayPan()
    {
        var cursor = VrUiCursor.I;
        var map = _map;
        var down = cursor != null && cursor.IsPointerDown;
        var pressStarted = down && !_wasPointerDown;
        _wasPointerDown = down;

        if (map == null || cursor == null || !down || !global::DynamicMap.mapMaximized ||
            !cursor.IsActive || !cursor.IsControllerModeActive || map.mapBackground == null || map.mapImage == null)
        {
            _panCandidate = false;
            _panning = false;
            return;
        }

        var camera = APIBus.CockpitHudCamera;
        var background = map.mapBackground.rectTransform;
        if (camera == null ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(background, cursor.GetScreenPoint(), camera, out var local))
        {
            return;
        }

        if (pressStarted)
        {
            _panCandidate = background.rect.Contains(local);
            _panning = false;
            _panStartLocal = local;
            _panLastLocal = local;
            return;
        }

        if (!_panCandidate) return;

        if (!_panning)
        {
            var startDistance = background.rect.width * PanStartFraction;
            if ((local - _panStartLocal).sqrMagnitude < startDistance * startDistance) return;
            _panning = true;
        }

        var delta = local - _panLastLocal;
        _panLastLocal = local;
        var imageScale = map.mapImage.transform.localScale.x;
        if (delta.sqrMagnitude < 1e-6f || imageScale < 1e-4f) return;

        // DynamicMap places the map image at (-stationaryOffset - positionOffset) * imageScale in the background's
        // space, then clamps and applies that itself every frame, so moving the image by delta means this:
        PositionOffsetField(map) -= delta / imageScale;
        (MapChangedField?.GetValue(null) as Action)?.Invoke();
    }

    private void OnDestroy()
    {
        if (_canvas != null)
            VrCanvasHitTester.Unregister(_canvas);
    }
}
