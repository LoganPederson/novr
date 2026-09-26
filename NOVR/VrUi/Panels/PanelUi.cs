using System;
using NOVR.VrUi.Native;
using UnityEngine;
using UnityEngine.UI;

namespace NOVR.VrUi.Panels;

// Small uGUI builders for the floating panels' controls.
internal static class PanelUi
{
    private static Font? _font;

    public static Button CreateButton(string label, RectTransform parent, Vector2 anchoredPosition, Vector2 size, Color color, Action onClick)
    {
        var rect = CreateImage(label, parent, color, anchoredPosition, size);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = rect.GetComponent<Image>();
        button.onClick.AddListener(() => onClick());
        NativeButtonFeedback.Configure(button, color);
        CreateText($"{label} Text", rect, label, size, 22);
        return button;
    }

    public static RectTransform CreateImage(string name, RectTransform parent, Color color, Vector2 anchoredPosition, Vector2 size)
    {
        var gameObject = new GameObject(name);
        gameObject.transform.SetParent(parent, false);
        var rect = gameObject.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;
        var image = gameObject.AddComponent<Image>();
        image.color = color;
        return rect;
    }

    public static Text CreateText(string name, RectTransform parent, string text, Vector2 size, int fontSize)
    {
        var gameObject = new GameObject(name);
        gameObject.transform.SetParent(parent, false);
        var rect = gameObject.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        var textComponent = gameObject.AddComponent<Text>();
        textComponent.text = text;
        textComponent.font = GetFont();
        textComponent.fontSize = fontSize;
        textComponent.alignment = TextAnchor.MiddleCenter;
        textComponent.color = Color.white;
        textComponent.raycastTarget = false;
        return textComponent;
    }

    // Unity 2022.2 renamed the built-in font; try the new name first and fall back to the old one.
    private static Font? GetFont()
    {
        if (_font != null) return _font;
        foreach (var name in new[] { "LegacyRuntime.ttf", "Arial.ttf" })
        {
            try
            {
                _font = Resources.GetBuiltinResource<Font>(name);
                if (_font != null) return _font;
            }
            catch (ArgumentException)
            {
            }
        }
        return null;
    }
}
