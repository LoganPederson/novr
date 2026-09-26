using System;

namespace NOVR.Diagnostics;

// One frame's worth of timings. Sources that are unavailable are NaN so averages can skip them
// instead of mixing in zeros.
internal struct FrameSample
{
    public float FrameMs;
    public float CpuMainMs;
    public float CpuRenderMs;
    public float GpuMs;
    public float XrAppGpuMs;
    public float XrCompositorGpuMs;
    public int XrDroppedFrames;
    public bool XrRepeatedFrame;
    public float RefreshRate;
}

// Accumulates frame samples over a time window and produces averages, a 95th percentile frame time
// and counts of slow/dropped frames. Allocation-free after construction.
internal sealed class FrameStatsWindow
{
    // Frames beyond this in one window only drop out of the percentile, the averages still count them.
    private const int MaxPercentileFrames = 2048;

    private readonly float[] _frameTimes = new float[MaxPercentileFrames];
    private readonly float[] _sortBuffer = new float[MaxPercentileFrames];

    private Average _frameMs;
    private Average _cpuMainMs;
    private Average _cpuRenderMs;
    private Average _gpuMs;
    private Average _xrAppGpuMs;
    private Average _xrCompositorGpuMs;
    private float _maxFrameMs;
    private float _elapsedSeconds;
    private float _refreshRate = float.NaN;
    private int _frames;
    private int _slowFrames;
    private int _droppedFrames;
    private int _repeatedFrames;

    public float ElapsedSeconds => _elapsedSeconds;
    public int Frames => _frames;
    public float Fps => _elapsedSeconds > 0f ? _frames / _elapsedSeconds : float.NaN;
    public float FrameMsAverage => _frameMs.Value;
    public float FrameMsMax => _frames > 0 ? _maxFrameMs : float.NaN;
    public float CpuMainMs => _cpuMainMs.Value;
    public float CpuRenderMs => _cpuRenderMs.Value;
    public float GpuMs => _gpuMs.Value;
    public float XrAppGpuMs => _xrAppGpuMs.Value;
    public float XrCompositorGpuMs => _xrCompositorGpuMs.Value;
    public float RefreshRate => _refreshRate;
    // Frames that took noticeably longer than one display refresh, so the runtime had to reproject.
    public int SlowFrames => _slowFrames;
    public int DroppedFrames => _droppedFrames;
    public int RepeatedFrames => _repeatedFrames;

    public void Add(in FrameSample sample)
    {
        _elapsedSeconds += sample.FrameMs / 1000f;
        if (_frames < MaxPercentileFrames) _frameTimes[_frames] = sample.FrameMs;
        _frames++;

        _frameMs.Add(sample.FrameMs);
        _cpuMainMs.Add(sample.CpuMainMs);
        _cpuRenderMs.Add(sample.CpuRenderMs);
        _gpuMs.Add(sample.GpuMs);
        _xrAppGpuMs.Add(sample.XrAppGpuMs);
        _xrCompositorGpuMs.Add(sample.XrCompositorGpuMs);
        if (sample.FrameMs > _maxFrameMs) _maxFrameMs = sample.FrameMs;

        if (sample.RefreshRate > 0f)
        {
            _refreshRate = sample.RefreshRate;
            if (sample.FrameMs > 1200f / sample.RefreshRate) _slowFrames++;
        }

        _droppedFrames += Math.Max(0, sample.XrDroppedFrames);
        if (sample.XrRepeatedFrame) _repeatedFrames++;
    }

    public float FrameMsPercentile(float percentile)
    {
        var count = Math.Min(_frames, MaxPercentileFrames);
        if (count == 0) return float.NaN;

        Array.Copy(_frameTimes, _sortBuffer, count);
        Array.Sort(_sortBuffer, 0, count);
        var index = (int)Math.Ceiling(percentile * count) - 1;
        return _sortBuffer[Math.Max(0, Math.Min(count - 1, index))];
    }

    public void Reset()
    {
        _frameMs = default;
        _cpuMainMs = default;
        _cpuRenderMs = default;
        _gpuMs = default;
        _xrAppGpuMs = default;
        _xrCompositorGpuMs = default;
        _maxFrameMs = 0f;
        _elapsedSeconds = 0f;
        _frames = 0;
        _slowFrames = 0;
        _droppedFrames = 0;
        _repeatedFrames = 0;
    }

    private struct Average
    {
        private double _sum;
        private int _count;

        public float Value => _count > 0 ? (float)(_sum / _count) : float.NaN;

        public void Add(float value)
        {
            if (float.IsNaN(value) || value <= 0f) return;
            _sum += value;
            _count++;
        }
    }
}
