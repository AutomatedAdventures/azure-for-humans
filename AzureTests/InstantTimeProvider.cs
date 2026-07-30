namespace AzureTests;

public class InstantTimeProvider : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        _now += dueTime;
        return new ImmediateTimer(callback, state);
    }

    private sealed class ImmediateTimer : ITimer
    {
        public ImmediateTimer(TimerCallback callback, object? state) => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
