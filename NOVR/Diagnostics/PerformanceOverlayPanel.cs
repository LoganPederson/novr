using System.Globalization;
using System.Text;
using NOVR.VrUi;
using UnityEngine;
using UnityEngine.UI;

namespace NOVR.Diagnostics;

// Small head-locked text panel in the lower left of the view, drawn by the VR UI camera so it sits on top
// of the world and cockpit like the rest of NOVR's HUD.
internal sealed class PerformanceOverlayPanel
{
    private const float Distance = 1.0f;
    private static readonly Vector3 LocalOffset = new(-0.34f, -0.28f, Distance);
    private static readonly Vector2 SizePixels = new(330f, 128f);
    private const float MetersPerPixel = 0.0009f;
    private const int FontSize = 20;

    private readonly StringBuilder _builder = new(256);
    private GameObject? _root;
    private Text? _text;

    public void SetVisible(bool visible)
    {
        if (!visible)
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
            return;
        }

        var hudCamera = NOUIManager.I != null ? NOUIManager.I.CockpitHudCamera : null;
        if (hudCamera == null) return;

        if (_root == null) Build();
        if (_root == null) return;

        // The HUD camera follows the head, so parenting to it keeps the panel fixed in view without
        // lagging a frame behind head motion.
        var root = _root.transform;
        if (root.parent != hudCamera.transform)
        {
            root.SetParent(hudCamera.transform, false);
            root.localPosition = LocalOffset;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one * MetersPerPixel;
            _root.GetComponent<Canvas>().worldCamera = hudCamera;
        }

        if (!_root.activeSelf) _root.SetActive(true);
    }

    public void Show(FrameStatsWindow window)
    {
        if (_text == null) return;

        var inv = CultureInfo.InvariantCulture;
        _builder.Clear();
        _builder.Append("FPS ").Append(Format(window.Fps, "0"))
            .Append("   frame ").Append(Format(window.FrameMsAverage, "0.0"))
            .Append(" ms (p95 ").Append(Format(window.FrameMsPercentile(0.95f), "0.0"))
            .Append(", max ").Append(Format(window.FrameMsMax, "0.0")).Append(")\n");
        _builder.Append("CPU main ").Append(Format(window.CpuMainMs, "0.0"))
            .Append("  render ").Append(Format(window.CpuRenderMs, "0.0"))
            .Append("  GPU ").Append(Format(window.GpuMs, "0.0")).Append(" ms\n");
        _builder.Append("XR app GPU ").Append(Format(window.XrAppGpuMs, "0.0"))
            .Append("  compositor ").Append(Format(window.XrCompositorGpuMs, "0.0")).Append(" ms\n");
        _builder.Append("Refresh ").Append(Format(window.RefreshRate, "0")).Append(" Hz")
            .Append("  slow ").Append(window.SlowFrames.ToString(inv))
            .Append("  dropped ").Append(window.DroppedFrames.ToString(inv))
            .Append("  repeated ").Append(window.RepeatedFrames.ToString(inv));

        _text.text = _builder.ToString();
    }

    private static string Format(float value, string format) =>
        float.IsNaN(value) ? "n/a" : value.ToString(format, CultureInfo.InvariantCulture);

    private void Build()
    {
        _root = new GameObject("NOVR Performance Overlay");
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 1000;
        ((RectTransform)_root.transform).sizeDelta = SizePixels;

        var background = new GameObject("Background");
        background.transform.SetParent(_root.transform, false);
        var backgroundRect = background.AddComponent<RectTransform>();
        Stretch(backgroundRect, 0f);
        var image = background.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.55f);
        image.raycastTarget = false;

        var textObject = new GameObject("Text");
        textObject.transform.SetParent(_root.transform, false);
        var textRect = textObject.AddComponent<RectTransform>();
        Stretch(textRect, 8f);
        _text = textObject.AddComponent<Text>();
        _text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        _text.fontSize = FontSize;
        _text.alignment = TextAnchor.UpperLeft;
        _text.color = new Color(0.7f, 1f, 0.7f, 1f);
        _text.horizontalOverflow = HorizontalWrapMode.Overflow;
        _text.verticalOverflow = VerticalWrapMode.Overflow;
        _text.raycastTarget = false;
        _text.text = "Measuring...";

        LayerHelper.SetLayerRecursive(_root.transform, LayerHelper.GetVrUiLayer());
    }

    private static void Stretch(RectTransform rect, float padding)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(padding, padding);
        rect.offsetMax = new Vector2(-padding, -padding);
    }

    public void Destroy()
    {
        if (_root != null) Object.Destroy(_root);
        _root = null;
        _text = null;
    }
}
