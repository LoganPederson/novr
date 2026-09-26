using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NOVR.VrUi.Panels;

// A floating copy of the cockpit's target screen (the TacScreen: radar/optical scan and the targeting camera),
// which the game only draws onto the instrument panel. It shows the same render texture the cockpit display
// uses, so it costs no extra rendering, and it can be placed and sized like the map.
internal sealed class TargetScreenPanel : MonoBehaviour
{
    private const float CanvasUnits = 512f;
    // Meters per canvas unit at size 1: about 40 cm across.
    private const float BaseScale = 0.0008f;
    private const float SearchInterval = 1f;

    private static readonly AccessTools.FieldRef<global::TacScreen, RenderTexture>? RenderTextureField =
        TryFieldRef<RenderTexture>("renderTexture");
    private static readonly AccessTools.FieldRef<global::TacScreen, Camera>? CameraField =
        TryFieldRef<Camera>("cam");

    private GameObject? _root;
    private RawImage? _image;
    private Text? _message;
    private FloatingPanel? _panel;
    private global::TacScreen? _tacScreen;
    private float _nextSearchTime;

    public bool Requested { get; private set; }
    public FloatingPanel? Panel => _panel;

    public void Toggle() => SetRequested(!Requested);

    public void SetRequested(bool requested)
    {
        Requested = requested;
        if (requested) _nextSearchTime = 0f;
    }

    private void Update()
    {
        var shouldShow = Requested && (ModConfiguration.Instance?.MovablePanels.Value ?? false) && IsInCockpit();
        if (!shouldShow)
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
            return;
        }

        EnsureRoot();
        if (!_root!.activeSelf) _root.SetActive(true);
        UpdateTexture();
    }

    private void OnDestroy()
    {
        if (_root != null) Destroy(_root);
    }

    private static bool IsInCockpit()
    {
        var combatHud = global::SceneSingleton<global::CombatHUD>.i;
        return combatHud != null && combatHud.aircraft != null && !combatHud.aircraft.disabled;
    }

    private void UpdateTexture()
    {
        if (_image == null || _message == null) return;

        if (_tacScreen == null && Time.unscaledTime >= _nextSearchTime)
        {
            _nextSearchTime = Time.unscaledTime + SearchInterval;
            _tacScreen = FindObjectOfType<global::TacScreen>();
        }

        Texture? texture = null;
        if (_tacScreen != null)
        {
            texture = RenderTextureField?.Invoke(_tacScreen);
            if (texture == null && CameraField != null)
            {
                var camera = CameraField(_tacScreen);
                if (camera != null) texture = camera.targetTexture;
            }
        }

        if (_image.texture != texture) _image.texture = texture;
        _image.color = texture != null ? Color.white : Color.black;
        _message.enabled = texture == null;

        // Keep the screen's shape: fit the texture into the square canvas.
        if (texture != null && texture.width > 0 && texture.height > 0)
        {
            var aspect = (float)texture.width / texture.height;
            _image.rectTransform.sizeDelta = aspect >= 1f
                ? new Vector2(CanvasUnits, CanvasUnits / aspect)
                : new Vector2(CanvasUnits * aspect, CanvasUnits);
        }
    }

    private void EnsureRoot()
    {
        if (_root != null) return;

        _root = new GameObject("NOVR Target Screen Panel");
        DontDestroyOnLoad(_root);
        var rect = _root.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(CanvasUnits, CanvasUnits);

        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = APIBus.CockpitHudCamera;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 4000;
        _root.AddComponent<GraphicRaycaster>();

        var imageRect = PanelUi.CreateImage("Screen", rect, Color.black, Vector2.zero, new Vector2(CanvasUnits, CanvasUnits));
        Destroy(imageRect.GetComponent<Image>());
        _image = imageRect.gameObject.AddComponent<RawImage>();
        // Hittable, so the cursor can land on the panel.
        _image.raycastTarget = true;

        _message = PanelUi.CreateText("Message", rect, "NO TARGET SCREEN", new Vector2(CanvasUnits, 80f), 32);

        LayerHelper.SetLayerRecursive(_root.transform, LayerHelper.GetVrUiLayer());

        var config = ModConfiguration.Instance;
        _panel = _root.AddComponent<FloatingPanel>();
        _panel.Configure(
            new PanelPlacementSettings(config.TargetScreenPanelDistance, config.TargetScreenPanelYaw, config.TargetScreenPanelPitch, config.TargetScreenPanelSize),
            BaseScale,
            () => _image != null ? _image.rectTransform : null,
            () => SetRequested(false));
    }

    private static AccessTools.FieldRef<global::TacScreen, T>? TryFieldRef<T>(string name)
    {
        try
        {
            return AccessTools.FieldRefAccess<global::TacScreen, T>(name);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[NOVR] TargetScreenPanel: TacScreen.{name} not found ({exception.Message}); the panel may stay blank.");
            return null;
        }
    }
}
