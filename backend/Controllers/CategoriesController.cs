using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/categories")]
[Authorize]
public class CategoriesController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var categories = await db.Categories
            .Where(c => c.UserId == UserId)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Color))
            .ToListAsync();

        return Ok(categories);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCategoryDto dto)
    {
        var category = new Category
        {
            UserId = UserId,
            Name = dto.Name,
            Color = dto.Color
        };

        db.Categories.Add(category);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), new CategoryDto(category.Id, category.Name, category.Color));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateCategoryDto dto)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == UserId);
        if (category is null) return NotFound();

        category.Name = dto.Name;
        category.Color = dto.Color;

        await db.SaveChangesAsync();
        return Ok(new CategoryDto(category.Id, category.Name, category.Color));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == UserId);
        if (category is null) return NotFound();

        db.Categories.Remove(category);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
