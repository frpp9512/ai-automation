using AiAutomations.Core.Models;

namespace AiAutomations.Core.Abstractions;

/// <summary>
/// Dispatches an approved ActionItem to the right side effect (Slack reply, Claude Code
/// session, ...) and updates its Status to Executed/Failed. Called from ActionsController
/// right after an action is approved.
/// </summary>
public interface IActionExecutor
{
    Task ExecuteAsync(ActionItem action, CancellationToken cancellationToken);
}
