using System.Diagnostics;

namespace Mdm.Services;

/// <summary>
/// Thread-safe, responsive Token Bucket Rate Limiter with debt queueing and dynamic rate changes.
/// Mirrors the IDM Turbo token-debt model to aggregate multi-worker throughput strictly to target rate.
/// </summary>
public class TokenBucketRateLimiter
{
    private readonly object _lock = new();
    private long _bytesPerSecond;
    private double _tokens;
    private long _lastCheckedTimestamp;
    private TaskCompletionSource<bool> _changeSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public long BytesPerSecond => Interlocked.Read(ref _bytesPerSecond);

    public TokenBucketRateLimiter(long bytesPerSecond = 0)
    {
        _bytesPerSecond = bytesPerSecond;
        _tokens = bytesPerSecond;
        _lastCheckedTimestamp = Stopwatch.GetTimestamp();
    }

    public void SetRate(long bytesPerSecond)
    {
        TaskCompletionSource<bool> oldSignal;

        lock (_lock)
        {
            _bytesPerSecond = bytesPerSecond;
            long now = Stopwatch.GetTimestamp();

            // Give a fresh burst capacity and clear accumulated debt
            _tokens = Math.Max(0, bytesPerSecond);
            _lastCheckedTimestamp = now;

            // Notify all waiting workers to wake up immediately
            oldSignal = _changeSignal;
            _changeSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        oldSignal.TrySetResult(true);
    }

    public async ValueTask ConsumeAsync(int bytes, CancellationToken ct = default)
    {
        if (bytes <= 0) return;

        int bytesToDeduct = bytes;

        while (!ct.IsCancellationRequested)
        {
            long limit = Interlocked.Read(ref _bytesPerSecond);
            if (limit <= 0) return; // Unlimited throughput

            TimeSpan? waitDuration = null;
            Task changeTask;

            lock (_lock)
            {
                long now = Stopwatch.GetTimestamp();
                double limitF64 = limit;
                double elapsed = Stopwatch.GetElapsedTime(_lastCheckedTimestamp, now).TotalSeconds;
                _lastCheckedTimestamp = now;

                // Replenish tokens capped to 1.0s allowance
                double maxCapacity = limitF64;
                _tokens = Math.Min(maxCapacity, _tokens + (elapsed * limitF64));

                if (bytesToDeduct > 0)
                {
                    _tokens -= bytesToDeduct;
                    bytesToDeduct = 0;
                }

                if (_tokens < 0.0)
                {
                    double debt = -_tokens;
                    double waitSecs = debt / limitF64;
                    // Cap wait to at most 2.0s per iteration to avoid indefinite stalls
                    waitDuration = TimeSpan.FromSeconds(Math.Min(waitSecs, 2.0));
                }

                changeTask = _changeSignal.Task;
            }

            if (waitDuration == null)
            {
                return; // Sufficient tokens available
            }

            // Wait for delay timer, rate change notification, or cancellation
            var delayTask = Task.Delay(waitDuration.Value, ct);
            var completedTask = await Task.WhenAny(delayTask, changeTask);
            if (completedTask == changeTask)
            {
                // Rate changed on the fly! Loop again to re-evaluate with new rate immediately
                continue;
            }

            // Delay completed
            break;
        }
    }
}
