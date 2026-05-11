using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimisticConcurrencyDemo.Data;
using OptimisticConcurrencyDemo.Models;

namespace OptimisticConcurrencyDemo.Controllers;

[ApiController]
[Route("api/coupons")]
public class CouponsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public Task<List<Coupon>> List(CancellationToken ct) =>
        db.Coupons.AsNoTracking().ToListAsync(ct);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Coupon>> Get(Guid id, CancellationToken ct)
    {
        var coupon = await db.Coupons.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        return coupon is null ? NotFound() : coupon;
    }

    public record CreateCouponRequest(
        string Code,
        int RedemptionsRemaining,
        string? Description,
        DateTimeOffset ExpiresAt);

    [HttpPost]
    public async Task<ActionResult<Coupon>> Create(CreateCouponRequest request, CancellationToken ct)
    {
        var coupon = new Coupon
        {
            Id = Guid.NewGuid(),
            Code = request.Code,
            RedemptionsRemaining = request.RedemptionsRemaining,
            Description = request.Description,
            ExpiresAt = request.ExpiresAt,
        };
        db.Coupons.Add(coupon);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = coupon.Id }, coupon);
    }

    public record UpdateCouponRequest(
        string Code,
        int RedemptionsRemaining,
        string? Description,
        DateTimeOffset ExpiresAt,
        Guid Version);

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<Coupon>> Update(Guid id, UpdateCouponRequest request, CancellationToken ct)
    {
        var coupon = await db.LoadForUpdateAsync<Coupon>(id, request.Version, ct);
        if (coupon is null) return NotFound();

        coupon.Code = request.Code;
        coupon.RedemptionsRemaining = request.RedemptionsRemaining;
        coupon.Description = request.Description;
        coupon.ExpiresAt = request.ExpiresAt;
        // Version bump happens in ConcurrencyInterceptor before SaveChanges.
        // DbUpdateConcurrencyException is mapped to 409 by ConcurrencyConflictExceptionFilter.
        await db.SaveChangesAsync(ct);

        return coupon;
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (coupon is null) return NotFound();

        db.Coupons.Remove(coupon);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private enum RedeemOutcome { Ok, NotFound, Exhausted }

    [HttpPost("{id:guid}/redemptions")]
    public async Task<IActionResult> Redeem(Guid id, CancellationToken ct)
    {
        var outcome = await db.ExecuteWithConcurrencyRetryAsync(async () =>
        {
            var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (coupon is null) return RedeemOutcome.NotFound;
            if (coupon.RedemptionsRemaining <= 0) return RedeemOutcome.Exhausted;

            coupon.RedemptionsRemaining -= 1;
            await db.SaveChangesAsync(ct);
            return RedeemOutcome.Ok;
        }, ct: ct);

        return outcome switch
        {
            RedeemOutcome.Ok => NoContent(),
            RedeemOutcome.NotFound => NotFound(),
            RedeemOutcome.Exhausted => UnprocessableEntity(new { error = "Coupon has no redemptions remaining" }),
            _ => StatusCode(500),
        };
    }
}
