namespace OptimisticConcurrencyDemo.Data;

public record ConcurrencyRetryPolicy
{
    public int MaxAttempts { get; init; } = 3;
    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromMilliseconds(50);
}
