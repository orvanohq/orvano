namespace Orvano.Testing;

/// <summary>
/// A clock that moves only when the test calls <see cref="Advance"/>, so the listener's and the job runner's deadlines
/// fire at a time the test chooses, not after a wall clock wait (spec 0013 LSN-3, JTS-1). Timers due at once fire on the
/// thread pool.
/// </summary>
public sealed class ManualClock : TimeProvider
{
    private readonly Lock _lock = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {
            return _now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves the clock forward and fires every timer that is due by then, once.</summary>
    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;
        lock (_lock)
        {
            _now += by;
            due = [.. _timers.Where(t => t.Due <= _now)];
            foreach (ManualTimer timer in due)
            {
                if (timer.Period == Timeout.InfiniteTimeSpan)
                {
                    _timers.Remove(timer);
                }
                else
                {
                    timer.Due = _now + timer.Period;
                }
            }
        }

        foreach (ManualTimer timer in due)
        {
            timer.Fire();
        }
    }

    private void Schedule(ManualTimer timer, TimeSpan dueTime, TimeSpan period)
    {
        lock (_lock)
        {
            _timers.Remove(timer);
            if (dueTime == Timeout.InfiniteTimeSpan)
            {
                return;
            }

            timer.Due = _now + dueTime;
            timer.Period = period;
            if (dueTime > TimeSpan.Zero)
            {
                _timers.Add(timer);
                return;
            }

            if (period != Timeout.InfiniteTimeSpan)
            {
                timer.Due = _now + period;
                _timers.Add(timer);
            }
        }

        ThreadPool.QueueUserWorkItem(_ => timer.Fire());
    }

    private void Cancel(ManualTimer timer)
    {
        lock (_lock)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        private volatile bool _disposed;

        public DateTimeOffset Due { get; set; }

        public TimeSpan Period { get; set; } = Timeout.InfiniteTimeSpan;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (_disposed)
            {
                return false;
            }

            clock.Schedule(this, dueTime, period);
            return true;
        }

        public void Fire()
        {
            if (!_disposed)
            {
                callback(state);
            }
        }

        public void Dispose()
        {
            _disposed = true;
            clock.Cancel(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
