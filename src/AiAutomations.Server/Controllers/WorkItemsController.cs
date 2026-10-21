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
public class WorkItemsController(AppDbContext db, IRealtimeNotifier notifier) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<WorkItemDto>>> GetAll()
    {
        var items = await db.WorkItems
            .OrderByDescending(w => w.CreatedAtUtc)
            .Select(w => w.ToDto())
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost]
    public async Task<ActionResult<WorkItemDto>> Create([FromBody] UpsertWorkItemRequest request)
    {
        var item = new WorkItem
        {
            Title = request.Title,
            Description = request.Description,
            Priority = request.Priority,
            Type = request.Type,
            RepositoryName = request.RepositoryName,
            Labels = request.Labels,
            DueAtUtc = request.DueAtUtc
        };

        db.WorkItems.Add(item);
        await db.SaveChangesAsync();
        await notifier.WorkItemChangedAsync(item);
        return Ok(item.ToDto());
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WorkItemDto>> Update(Guid id, [FromBody] UpdateWorkItemRequest request)
    {
        var item = await db.WorkItems.FindAsync(id);
        if (item is null) return NotFound();

        item.Title = request.Title;
        item.Description = request.Description;
        item.Priority = request.Priority;
        item.Type = request.Type;
        item.RepositoryName = request.RepositoryName;
        item.Labels = request.Labels;
        item.DueAtUtc = request.DueAtUtc;
        item.PercentComplete = Math.Clamp(request.PercentComplete, 0, 100);
        item.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();
        await notifier.WorkItemChangedAsync(item);
        return Ok(item.ToDto());
    }

    [HttpGet("{id:guid}/comments")]
    public async Task<ActionResult<List<WorkItemCommentDto>>> GetComments(Guid id)
    {
        var comments = await db.WorkItemComments
            .Where(c => c.WorkItemId == id)
            .OrderBy(c => c.CreatedAtUtc)
            .Select(c => c.ToDto())
            .ToListAsync();

        return Ok(comments);
    }

    [HttpPost("{id:guid}/comments")]
    public async Task<ActionResult<WorkItemCommentDto>> AddComment(Guid id, [FromBody] AddWorkItemCommentRequest request)
    {
        var comment = new WorkItemComment { WorkItemId = id, Text = request.Text };
        db.WorkItemComments.Add(comment);
        await db.SaveChangesAsync();
        return Ok(comment.ToDto());
    }

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<WorkItemDto>> UpdateStatus(Guid id, [FromBody] UpdateWorkItemStatusRequest request)
    {
        var item = await db.WorkItems.FindAsync(id);
        if (item is null) return NotFound();

        item.Status = request.Status;
        item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        item.CompletedAtUtc = request.Status is WorkItemStatus.Done or WorkItemStatus.Cancelled
            ? (item.CompletedAtUtc ?? DateTimeOffset.UtcNow)
            : null;

        await db.SaveChangesAsync();
        await notifier.WorkItemChangedAsync(item);
        return Ok(item.ToDto());
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var item = await db.WorkItems.FindAsync(id);
        if (item is null) return NotFound();

        db.WorkItems.Remove(item);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
