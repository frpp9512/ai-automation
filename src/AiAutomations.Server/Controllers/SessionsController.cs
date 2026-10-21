using AiAutomations.Infrastructure.Data;
using AiAutomations.Shared.Mapping;
using AiAutomations.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiAutomations.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SessionsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ClaudeSessionDto>>> GetRecent([FromQuery] int take = 50)
    {
        var sessions = await db.Sessions
            .OrderByDescending(s => s.StartedAtUtc)
            .Take(take)
            .Select(s => s.ToDto())
            .ToListAsync();

        return Ok(sessions);
    }
}
