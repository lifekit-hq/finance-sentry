namespace FinanceSentry.Modules.BankSync.API.Controllers;

using FinanceSentry.Modules.BankSync.API.Responses;
using FinanceSentry.Modules.BankSync.Application.Services.CategoryMapping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Exposes the canonical category reference list so the frontend resolves labels from a
/// single source of truth instead of hardcoding them.
/// </summary>
[ApiController]
[Authorize]
[Route("categories")]
public class CategoriesController(ICategoryReadService categories) : ControllerBase
{
    private readonly ICategoryReadService _categories = categories ?? throw new ArgumentNullException(nameof(categories));

    // ── GET /api/v1/categories ────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var items = await _categories.GetAllAsync(ct);
        var dtos = items.Select(c => new CategoryDto(c.Key, c.Label, c.SortOrder)).ToList();
        return Ok(dtos);
    }
}
