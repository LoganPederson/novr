using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace NOVR.Diagnostics;

// Writes one row of averaged frame timings per interval, for before/after comparisons of settings.
internal sealed class PerformanceCsvLog : IDisposable
{
    private const string Header =
        "time,elapsed_s,scene,aircraft,frames,fps,frame_ms_avg,frame_ms_p95,frame_ms_max," +
        "cpu_main_ms,cpu_render_ms,gpu_ms,xr_app_gpu_ms,xr_compositor_gpu_ms," +
        "refresh_hz,slow_frames,xr_dropped_frames,xr_repeated_frames,eye_texture_width,eye_texture_height,eye_texture_scale";

    private readonly StreamWriter _writer;
    private readonly float _startTime;

    private PerformanceCsvLog(StreamWriter writer)
    {
        _writer = writer;
        _startTime = Time.unscaledTime;
    }

    public static PerformanceCsvLog? Open()
    {
        try
        {
            var directory = Path.Combine(Application.persistentDataPath, "NOVR");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"performance-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            var writer = new StreamWriter(path, append: false);
            writer.WriteLine(Header);
            writer.Flush();
            Debug.Log($"[NOVR] Logging performance to {path}");
            return new PerformanceCsvLog(writer);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[NOVR] Could not start the performance CSV log: {exception.Message}");
            return null;
        }
    }

    public void Write(FrameStatsWindow window)
    {
        var inv = CultureInfo.InvariantCulture;
        var line = string.Join(",",
            DateTime.Now.ToString("HH:mm:ss", inv),
            (Time.unscaledTime - _startTime).ToString("0.0", inv),
            Quote(SceneManager.GetActiveScene().name),
            Quote(Core.CurrentAircraftId ?? ""),
            window.Frames.ToString(inv),
            Number(window.Fps),
            Number(window.FrameMsAverage),
            Number(window.FrameMsPercentile(0.95f)),
            Number(window.FrameMsMax),
            Number(window.CpuMainMs),
            Number(window.CpuRenderMs),
            Number(window.GpuMs),
            Number(window.XrAppGpuMs),
            Number(window.XrCompositorGpuMs),
            Number(window.RefreshRate),
            window.SlowFrames.ToString(inv),
            window.DroppedFrames.ToString(inv),
            window.RepeatedFrames.ToString(inv),
            XRSettings.eyeTextureWidth.ToString(inv),
            XRSettings.eyeTextureHeight.ToString(inv),
            Number(XRSettings.eyeTextureResolutionScale));

        try
        {
            _writer.WriteLine(line);
            _writer.Flush();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[NOVR] Could not write to the performance CSV log: {exception.Message}");
        }
    }

    // Empty cells for values the game or runtime did not report.
    private static string Number(float value) =>
        float.IsNaN(value) ? "" : value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    public void Dispose()
    {
        try
        {
            _writer.Dispose();
        }
        catch (Exception)
        {
            // Closing a log that already failed is not worth reporting.
        }
    }
}
