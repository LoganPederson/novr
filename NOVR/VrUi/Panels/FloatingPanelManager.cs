using UnityEngine;

namespace NOVR.VrUi.Panels;

// Sets up the floating panels (the full map in flight and the target screen copy) and handles their shortcuts.
internal sealed class FloatingPanelManager : MonoBehaviour
{
    // DynamicMap parents the map to this canvas when it maximizes; at size 1 it keeps the 3 m, 0.003 placement
    // NOVRGameplayUIBehaviour gives it, so nothing moves until the player moves it.
    private const float MapBaseScale = 0.003f;

    private TargetScreenPanel? _targetScreen;
    private FloatingPanel? _mapPanel;
    private global::DynamicMap? _mapPanelOwner;

    private void Awake()
    {
        _targetScreen = gameObject.AddComponent<TargetScreenPanel>();
    }

    private void Update()
    {
        EnsureMapPanel();
        HandleShortcuts();
    }

    private void EnsureMapPanel()
    {
        var map = global::SceneSingleton<global::DynamicMap>.i;
        if (map == _mapPanelOwner && (_mapPanel != null || map == null)) return;
        if (map == null || map.maximizedMapCanvas == null) return;

        var canvasObject = map.maximizedMapCanvas.gameObject;
        if (!canvasObject.TryGetComponent<FloatingPanel>(out var panel)) panel = canvasObject.AddComponent<FloatingPanel>();
        _mapPanel = panel;
        _mapPanelOwner = map;

        var config = ModConfiguration.Instance;
        panel.Configure(
            new PanelPlacementSettings(config.MapPanelDistance, config.MapPanelYaw, config.MapPanelPitch, config.MapPanelSize),
            MapBaseScale,
            () => map != null && map.mapBackground != null ? map.mapBackground.rectTransform : null,
            () =>
            {
                if (map != null) map.Minimize();
            },
            () => global::DynamicMap.mapMaximized && IsFlying());
        panel.AddButton("TGT SCREEN", () => _targetScreen?.Toggle());
    }

    private static bool IsFlying()
    {
        var combatHud = global::SceneSingleton<global::CombatHUD>.i;
        return combatHud != null && combatHud.aircraft != null && !combatHud.aircraft.disabled;
    }

    private void HandleShortcuts()
    {
        var config = ModConfiguration.Instance;
        if (config == null) return;

        if (config.ToggleMapShortcut.Value.IsDown()) ToggleMap();
        if (config.ToggleTargetScreenPanelShortcut.Value.IsDown()) _targetScreen?.Toggle();

        if (config.BringPanelsToViewShortcut.Value.IsDown())
        {
            foreach (var panel in FloatingPanel.All.ToArray())
            {
                if (panel.IsShown) panel.BringToView();
            }
        }

        if (config.GrowPanelShortcut.Value.IsDown()) GetFocusedPanel()?.Grow();
        if (config.ShrinkPanelShortcut.Value.IsDown()) GetFocusedPanel()?.Shrink();
    }

    // Same rules as the game's own map key, minus the mission editor, which has its own map tab.
    private static void ToggleMap()
    {
        var map = global::SceneSingleton<global::DynamicMap>.i;
        if (map == null || global::GameManager.gameState == global::GameState.Editor) return;
        if (global::NuclearOption.MissionEditorScripts.InputFieldChecker.InsideInputField) return;

        if (global::DynamicMap.mapMaximized) map.Minimize();
        else map.Maximize();
    }

    // The panel under the cursor, else the map, else the target screen.
    private FloatingPanel? GetFocusedPanel()
    {
        var activeCanvas = VrUiCursor.I?.ActiveCanvas;
        if (activeCanvas != null)
        {
            var rootCanvas = activeCanvas.rootCanvas != null ? activeCanvas.rootCanvas : activeCanvas;
            if (rootCanvas.TryGetComponent<FloatingPanel>(out var underCursor) && underCursor.IsShown) return underCursor;
        }

        if (_mapPanel != null && _mapPanel.IsShown) return _mapPanel;
        var targetPanel = _targetScreen?.Panel;
        return targetPanel != null && targetPanel.IsShown ? targetPanel : null;
    }
}
