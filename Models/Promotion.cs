namespace OptimisticConcurrencyDemo.Models;

public class Promotion : Entity
{
    public string Name { get; set; } = "";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
}
