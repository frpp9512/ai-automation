using AiAutomations.Core.Abstractions;
using AiAutomations.Infrastructure.Data;
using AiAutomations.Shared.Mapping;
using AiAutomations.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiAutomations.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MessagesController(AppDbContext db, IBackfillRunner backfillRunner) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<SlackMessageDto>>> GetRecent([FromQuery] int take = 50)
    {
        var messages = await db.Messages
            .OrderByDescending(m => m.ReceivedAtUtc)
            .Take(take)
            .Select(m => m.ToDto())
            .ToListAsync();

        return Ok(messages);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SlackMessageDto>> GetById(Guid id)
    {
        var message = await db.Messages.FindAsync(id);
        return message is null ? NotFound() : Ok(message.ToDto());
    }

    /// <summary>
    /// Catch-up: finds messages sent to/mentioning the watched user in [From, To] that
    /// arrived before this service was listening, and runs them through the same
    /// classify-and-derive-actions pipeline as a live message. Requires Slack:UserToken
    /// to be configured (search.messages needs a user token).
    /// </summary>
    [HttpPost("backfill")]
    public async Task<ActionResult<BackfillResultDto>> Backfill([FromBody] BackfillRequest request)
    {
        var outcome = await backfillRunner.RunAsync(request.From, request.To, HttpContext.RequestAborted);
        return Ok(new BackfillResultDto(outcome.MessagesFound, outcome.NewMessages, outcome.Skipped));
    }
}
