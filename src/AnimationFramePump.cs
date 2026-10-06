using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FloatScreen;

internal sealed class AnimationFramePump : IDisposable
{
    private readonly SynchronizationContext _context;
    private readonly Action _frame;
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private volatile bool _running, _disposed;
    private int _pending;

    public AnimationFramePump(Action frame)
    {
        _context = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _frame = frame;
        _thread = new Thread(Loop) { IsBackground = true, Name = "FloatScreen animation" };
        _thread.Start();
    }

    public void Start() { if (_disposed) return; _running = true; _wake.Set(); }
    public void Stop() => _running = false;

    private void Loop()
    {
        var timer = CreateWaitableTimerEx(0, null, 0x2, 0x1F0003);
        if (timer == 0) timer = CreateWaitableTimerEx(0, null, 0, 0x1F0003);
        var step = Stopwatch.Frequency / 60;
        var next = Stopwatch.GetTimestamp();
        try
        {
            while (!_disposed)
            {
                if (!_running)
                {
                    _wake.WaitOne();
                    next = Stopwatch.GetTimestamp();
                    continue;
                }
                next += step;
                var remaining = next - Stopwatch.GetTimestamp();
                if (remaining > 0)
                {
                    var due = -Math.Max(1L, (long)(remaining * (10_000_000d / Stopwatch.Frequency)));
                    if (timer != 0 && SetWaitableTimer(timer, ref due, 0, 0, 0, false)) WaitForSingleObject(timer, 1000);
                    else Thread.Sleep(Math.Max(1, (int)(remaining * 1000d / Stopwatch.Frequency)));
                }
                if (_disposed || !_running) continue;
                var now = Stopwatch.GetTimestamp();
                if (now - next > step) next = now;
                // At most one frame may wait in the UI queue; slow frames never create a backlog.
                if (Interlocked.CompareExchange(ref _pending, 1, 0) != 0) continue;
                try
                {
                    _context.Post(_ =>
                    {
                        try { if (!_disposed && _running) _frame(); }
                        finally { Volatile.Write(ref _pending, 0); }
                    }, null);
                }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.InvalidAsynchronousStateException)
                {
                    Volatile.Write(ref _pending, 0);
                    _running = false;
                }
            }
        }
        finally
        {
            if (timer != 0) CloseHandle(timer);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _running = false; _wake.Set();
        if (_thread.Join(1500)) _wake.Dispose();
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWaitableTimerEx(nint attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(nint timer, ref long due, int period, nint completion, nint argument, [MarshalAs(UnmanagedType.Bool)] bool resume);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
}
