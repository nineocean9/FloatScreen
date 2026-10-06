using System.Diagnostics;

namespace FloatScreen;

internal sealed class MediaAnimationClock
{
    private double _time, _speed, _videoRate = 1;
    private long _stamp = Stopwatch.GetTimestamp();
    private bool _running, _initialized;

    public double Time => _time + (_running ? Math.Min(1.5, Stopwatch.GetElapsedTime(_stamp).TotalSeconds) * _speed : 0);

    public void Update(double time, double rate, bool running, bool reset)
    {
        var current = Time;
        if (!_initialized || reset || running != _running || Math.Abs(rate - _videoRate) > 0.001 || !running)
        {
            _time = time;
            _speed = rate;
        }
        else
        {
            // Keep position continuous. Correct small sampling drift through speed,
            // instead of snapping every 200 ms and occasionally moving text backwards.
            _time = current;
            _speed = rate + Math.Clamp((time - current) / 0.8, -rate * 0.12, rate * 0.12);
        }
        _videoRate = rate; _running = running; _initialized = true;
        _stamp = Stopwatch.GetTimestamp();
    }
}
