namespace OptimisticConcurrencyDemo.Models;

public class Coupon : Entity
{
    public string Code { get; set; } = "";
    public int RedemptionsRemaining { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
