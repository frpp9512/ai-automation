using AiAutomations.Core.Abstractions;
using AiAutomations.Core.Models;
using AiAutomations.Infrastructure.Data;
using AiAutomations.Shared.Mapping;
using AiAutomations.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiAutomations.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ActionsController(AppDbContext db, IRealtimeNotifier notifier, IActionExecutor executor) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ActionItemDto>>> GetPending()
    {
        var actions = await db.Actions
            .Where(a => a.Status == ActionStatus.PendingApproval)
            .OrderBy(a => a.CreatedAtUtc)
            .Select(a => a.ToDto())
            .ToListAsync();

        return Ok(actions);
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<ActionItemDto>> Approve(Guid id, [FromBody] ApproveActionRequest request)
    {
        var action = await db.Actions.FindAsync(id);
        if (action is null) return NotFound();

        if (request.EditedPayload is not null)
        {
            action.Payload = request.EditedPayload;
        }

        action.Status = ActionStatus.Approved;
        action.ResolvedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await notifier.ActionStatusChangedAsync(action);

        // Flips action.Status to Executed/Failed itself and notifies again.
        await executor.ExecuteAsync(action, HttpContext.RequestAborted);

        return Ok(action.ToDto());
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<ActionItemDto>> Reject(Guid id, [FromBody] RejectActionRequest request)
    {
        var action = await db.Actions.FindAsync(id);
        if (action is null) return NotFound();

        action.Status = ActionStatus.Rejected;
        action.ResolvedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await notifier.ActionStatusChangedAsync(action);

        return Ok(action.ToDto());
    }
}
