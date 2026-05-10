using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimisticConcurrencyDemo.Data;
using OptimisticConcurrencyDemo.Models;

namespace OptimisticConcurrencyDemo.Controllers;

[ApiController]
[Route("api/promotions")]
public class PromotionsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public Task<List<Promotion>> List(CancellationToken ct) =>
        db.Promotions.AsNoTracking().ToListAsync(ct);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Promotion>> Get(Guid id, CancellationToken ct)
    {
        var promotion = await db.Promotions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return promotion is null ? NotFound() : promotion;
    }

    public record CreatePromotionRequest(
        string Name,
        DateTimeOffset StartsAt,
        DateTimeOffset EndsAt);

    [HttpPost]
    public async Task<ActionResult<Promotion>> Create(CreatePromotionRequest request, CancellationToken ct)
    {
        var promotion = new Promotion
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
        };
        db.Promotions.Add(promotion);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = promotion.Id }, promotion);
    }

    public record UpdatePromotionRequest(
        string Name,
        DateTimeOffset StartsAt,
        DateTimeOffset EndsAt,
        Guid Version);

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<Promotion>> Update(Guid id, UpdatePromotionRequest request, CancellationToken ct)
    {
        var promotion = await db.Promotions.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (promotion is null) return NotFound();

        // Same concurrency pattern as CouponsController: route the client's claimed version into EF's Original.
        db.Entry(promotion).Property(p => p.Version).OriginalValue = request.Version;

        promotion.Name = request.Name;
        promotion.StartsAt = request.StartsAt;
        promotion.EndsAt = request.EndsAt;

        await db.SaveChangesAsync(ct);

        return promotion;
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var promotion = await db.Promotions.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (promotion is null) return NotFound();

        db.Promotions.Remove(promotion);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
