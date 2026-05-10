namespace OptimisticConcurrencyDemo.Models;

public abstract class Entity
{
    public Guid Id { get; set; }
    public Guid Version { get; set; }
}
