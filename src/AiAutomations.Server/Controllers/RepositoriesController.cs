using AiAutomations.Core.Models;
using AiAutomations.Infrastructure.Data;
using AiAutomations.Shared.Mapping;
using AiAutomations.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiAutomations.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RepositoriesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<RepositoryProfileDto>>> GetAll()
    {
        var repos = await db.Repositories
            .OrderBy(r => r.Name)
            .Select(r => r.ToDto())
            .ToListAsync();

        return Ok(repos);
    }

    [HttpPost]
    public async Task<ActionResult<RepositoryProfileDto>> Create([FromBody] UpsertRepositoryRequest request)
    {
        var repo = new RepositoryProfile { Name = request.Name, Path = request.Path, Keywords = request.Keywords };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();
        return Ok(repo.ToDto());
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<RepositoryProfileDto>> Update(Guid id, [FromBody] UpsertRepositoryRequest request)
    {
        var repo = await db.Repositories.FindAsync(id);
        if (repo is null) return NotFound();

        repo.Name = request.Name;
        repo.Path = request.Path;
        repo.Keywords = request.Keywords;
        await db.SaveChangesAsync();
        return Ok(repo.ToDto());
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var repo = await db.Repositories.FindAsync(id);
        if (repo is null) return NotFound();

        db.Repositories.Remove(repo);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
