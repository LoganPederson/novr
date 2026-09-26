using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace NOVR.Diagnostics;

// Samples frame timings for the performance overlay and the CSV log. When both are off it only checks
// the toggle shortcut each frame, so leaving it installed costs next to nothing.
public class PerformanceMonitor : MonoBehaviour
{
    private const float OverlayRefreshSeconds = 0.5f;
    private const float DisplaySubsystemLookupSeconds = 1f;

    private static PerformanceMonitor? _instance;

    private readonly FrameTiming[] _frameTimings = new FrameTiming[1];
    private readonly List<XRDisplaySubsystem> _displaySubsystems = new();
    private readonly FrameStatsWindow _overlayWindow = new();
    private readonly FrameStatsWindow _csvWindow = new();

    private PerformanceOverlayPanel? _panel;
    private PerformanceCsvLog? _csvLog;
    private XRDisplaySubsystem? _display;
    private float _nextDisplayLookupTime;
    private bool _wasSampling;
    private bool _loggedTimingSupport;

    public static void EnsureCreated()
    {
        if (_instance != null) return;

        var gameObject = new GameObject("NOVR Performance Monitor");
        DontDestroyOnLoad(gameObject);
        _instance = gameObject.AddComponent<PerformanceMonitor>();
    }

    private void Update()
    {
        var config = ModConfiguration.Instance;
        if (config == null) return;

        if (Input.GetKeyDown(config.PerformanceOverlayShortcut.Value))
        {
            config.ShowPerformanceOverlay.Value = !config.ShowPerformanceOverlay.Value;
        }

        var showOverlay = config.ShowPerformanceOverlay.Value;
        var logCsv = config.LogPerformanceCsv.Value;
        UpdatePanelVisibility(showOverlay);
        UpdateCsvLogState(logCsv);

        if (!showOverlay && !logCsv)
        {
            _wasSampling = false;
            return;
        }

        if (!_wasSampling)
        {
            // Start clean so a window never spans the time sampling was off.
            _overlayWindow.Reset();
            _csvWindow.Reset();
            _wasSampling = true;
        }

        var sample = TakeSample();

        if (showOverlay)
        {
            _overlayWindow.Add(sample);
            if (_overlayWindow.ElapsedSeconds >= OverlayRefreshSeconds)
            {
                _panel?.Show(_overlayWindow);
                _overlayWindow.Reset();
            }
        }

        if (logCsv && _csvLog != null)
        {
            _csvWindow.Add(sample);
            if (_csvWindow.ElapsedSeconds >= config.PerformanceCsvInterval.Value)
            {
                _csvLog.Write(_csvWindow);
                _csvWindow.Reset();
            }
        }
    }

    private FrameSample TakeSample()
    {
        var sample = new FrameSample
        {
            FrameMs = Time.unscaledDeltaTime * 1000f,
            CpuMainMs = float.NaN,
            CpuRenderMs = float.NaN,
            GpuMs = float.NaN,
            XrAppGpuMs = float.NaN,
            XrCompositorGpuMs = float.NaN,
            RefreshRate = float.NaN
        };

        // Unity's frame timing stats only report when the game was built with them enabled (or the
        // platform always provides them); otherwise these stay NaN and show as n/a.
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, _frameTimings) > 0)
        {
            var timing = _frameTimings[0];
            sample.CpuMainMs = (float)timing.cpuMainThreadFrameTime;
            sample.CpuRenderMs = (float)timing.cpuRenderThreadFrameTime;
            sample.GpuMs = (float)timing.gpuFrameTime;
        }

        var display = GetDisplaySubsystem();
        if (display != null)
        {
            if (display.TryGetAppGPUTimeLastFrame(out var appGpu)) sample.XrAppGpuMs = appGpu * 1000f;
            if (display.TryGetCompositorGPUTimeLastFrame(out var compositorGpu)) sample.XrCompositorGpuMs = compositorGpu * 1000f;
            if (display.TryGetDroppedFrameCount(out var dropped)) sample.XrDroppedFrames = dropped;
            if (display.TryGetFramePresentCount(out var presentCount)) sample.XrRepeatedFrame = presentCount > 1;
            if (display.TryGetDisplayRefreshRate(out var refreshRate)) sample.RefreshRate = refreshRate;
        }

        if (float.IsNaN(sample.RefreshRate) && XRSettings.isDeviceActive && XRDevice.refreshRate > 0f)
        {
            sample.RefreshRate = XRDevice.refreshRate;
        }

        LogTimingSupportOnce(sample, display);
        return sample;
    }

    private XRDisplaySubsystem? GetDisplaySubsystem()
    {
        if (_display != null && _display.running) return _display;
        if (Time.unscaledTime < _nextDisplayLookupTime) return null;

        _nextDisplayLookupTime = Time.unscaledTime + DisplaySubsystemLookupSeconds;
        _display = null;
        SubsystemManager.GetSubsystems(_displaySubsystems);
        foreach (var subsystem in _displaySubsystems)
        {
            if (!subsystem.running) continue;
            _display = subsystem;
            break;
        }

        return _display;
    }

    // Record once which sources report anything, so a CSV full of n/a can be explained from the log.
    private void LogTimingSupportOnce(in FrameSample sample, XRDisplaySubsystem? display)
    {
        if (_loggedTimingSupport || Time.frameCount < 120) return;
        _loggedTimingSupport = true;

        Debug.Log("[NOVR] Performance monitor sources: " +
                  $"frameTimingFeature={FrameTimingManager.IsFeatureEnabled()}, " +
                  $"cpuMain={Describe(sample.CpuMainMs)}, gpu={Describe(sample.GpuMs)}, " +
                  $"xrDisplay={(display != null ? "running" : "none")}, xrAppGpu={Describe(sample.XrAppGpuMs)}, " +
                  $"xrCompositorGpu={Describe(sample.XrCompositorGpuMs)}, refreshRate={Describe(sample.RefreshRate)}");
    }

    private static string Describe(float value) => float.IsNaN(value) || value <= 0f ? "n/a" : value.ToString("0.00");

    private void UpdatePanelVisibility(bool showOverlay)
    {
        if (showOverlay)
        {
            _panel ??= new PerformanceOverlayPanel();
            _panel.SetVisible(true);
        }
        else
        {
            _panel?.SetVisible(false);
        }
    }

    private void UpdateCsvLogState(bool logCsv)
    {
        if (logCsv && _csvLog == null)
        {
            _csvLog = PerformanceCsvLog.Open();
            _csvWindow.Reset();
            // Turn the setting back off rather than retrying the file every frame.
            if (_csvLog == null) ModConfiguration.Instance.LogPerformanceCsv.Value = false;
        }
        else if (!logCsv && _csvLog != null)
        {
            _csvLog.Dispose();
            _csvLog = null;
        }
    }

    private void OnDestroy()
    {
        _csvLog?.Dispose();
        _csvLog = null;
        _panel?.Destroy();
        _panel = null;
        if (_instance == this) _instance = null;
    }
}
