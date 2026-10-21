using AiAutomations.Infrastructure.Data;
using AiAutomations.Shared.Mapping;
using AiAutomations.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiAutomations.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DecisionsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<MessageDecisionDto>>> GetRecent([FromQuery] int take = 50)
    {
        var decisions = await db.Decisions
            .OrderByDescending(d => d.CreatedAtUtc)
            .Take(take)
            .Select(d => d.ToDto())
            .ToListAsync();

        return Ok(decisions);
    }
}
