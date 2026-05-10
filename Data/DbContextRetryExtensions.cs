using Microsoft.EntityFrameworkCore;

namespace OptimisticConcurrencyDemo.Data;

public static class DbContextRetryExtensions
{
    public static async Task<TResult> ExecuteWithConcurrencyRetryAsync<TResult>(
        this DbContext db,
        Func<Task<TResult>> operation,
        ConcurrencyRetryPolicy? policy = null,
        CancellationToken ct = default)
    {
        policy ??= new ConcurrencyRetryPolicy();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < policy.MaxAttempts)
            {
                foreach (var entry in ex.Entries)
                {
                    await entry.ReloadAsync(ct);
                }

                await Task.Delay(JitteredBackoff(policy.InitialBackoff, attempt), ct);
            }
        }
    }

    private static TimeSpan JitteredBackoff(TimeSpan initial, int attempt)
    {
        var exponential = initial * Math.Pow(2, attempt - 1);
        var jitter = 0.5 + Random.Shared.NextDouble() * 0.5; // 50%-100% of exponential
        return exponential * jitter;
    }
}
