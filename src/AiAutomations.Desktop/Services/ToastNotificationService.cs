using AiAutomations.Core.Models;
using AiAutomations.Desktop.Core;
using AiAutomations.Shared;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace AiAutomations.Desktop.Services;

/// <summary>
/// Native Windows toast when a new action enters PendingApproval - not on later Approved/
/// Rejected/Executed transitions of the same action, to avoid double-notifying.
/// </summary>
public class ToastNotificationService : IDisposable
{
    private readonly DesktopRealtimeNotifier _notifier;
    private readonly TriageAppService _triageAppService;
    private readonly DispatcherQueue _dispatcherQueue;

    /// <summary>Fired with the action id to deep-link to when the user clicks a toast.</summary>
    public event Action<Guid>? Activated;

    /// <summary>Fired with the Work Item id when the user clicks a "task updated" toast.</summary>
    public event Action<Guid>? WorkItemActivated;

    /// <summary>Fired when the user clicks a health-status-changed toast.</summary>
    public event Action? HealthActivated;

    /// <summary>
    /// AppNotificationManager.Default.Register() is called once, centrally, by App.xaml.cs
    /// before it checks whether this launch was itself a notification click - this service
    /// only reacts to notifications while already running.
    /// </summary>
    public ToastNotificationService(DesktopRealtimeNotifier notifier, TriageAppService triageAppService, DispatcherQueue dispatcherQueue)
    {
        _notifier = notifier;
        _triageAppService = triageAppService;
        _dispatcherQueue = dispatcherQueue;

        AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
        _notifier.ActionStatusChanged += OnActionStatusChanged;
        _notifier.WorkItemCommentAdded += OnWorkItemCommentAdded;
    }

    private void OnActionStatusChanged(ActionItemDto action)
    {
        switch (action.Status)
        {
            case ActionStatus.PendingApproval:
                _ = ShowPendingToastAsync(action);
                break;
            case ActionStatus.Failed:
                // A Failed action drops out of the pending list entirely (it's no longer
                // PendingApproval) - without this, it would just silently vanish with no
                // explanation of what happened or why.
                ShowFailedToast(action);
                break;
        }
    }

    private async Task ShowPendingToastAsync(ActionItemDto action)
    {
        // The decision's Summary is always plain, human-written text - never show
        // action.Payload directly here, since for a TaskBrief it's a JSON blob.
        string? summary = null;
        try
        {
            var decision = await _triageAppService.GetDecisionAsync(action.DecisionId);
            summary = decision?.Summary;
        }
        catch
        {
            // Best-effort - fall back to a generic line below.
        }

        _dispatcherQueue.TryEnqueue(() =>
        {
            var builder = new AppNotificationBuilder()
                .AddArgument("actionId", action.Id.ToString())
                .AddText("New item needs your review")
                .AddText(!string.IsNullOrWhiteSpace(summary) ? summary : $"{action.Type} action ready for approval");

            AppNotificationManager.Default.Show(builder.BuildNotification());
        });
    }

    private void ShowFailedToast(ActionItemDto action)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            var builder = new AppNotificationBuilder()
                .AddArgument("actionId", action.Id.ToString())
                .AddText($"{action.Type} action failed")
                .AddText(!string.IsNullOrWhiteSpace(action.FailureReason) ? Truncate(action.FailureReason, 150) : "No further details were recorded.");

            AppNotificationManager.Default.Show(builder.BuildNotification());
        });
    }

    private void OnWorkItemCommentAdded(WorkItemDto workItem, WorkItemCommentDto comment)
    {
        // Only for automated (thread-linked) comments - a comment the user just typed
        // themselves needs no notification, they're already looking at it.
        if (!comment.IsAutomated)
        {
            return;
        }

        _dispatcherQueue.TryEnqueue(() =>
        {
            var builder = new AppNotificationBuilder()
                .AddArgument("workItemId", workItem.Id.ToString())
                .AddText("Task updated from Slack")
                .AddText($"{workItem.Title}: {Truncate(comment.Text, 120)}");

            AppNotificationManager.Default.Show(builder.BuildNotification());
        });
    }

    /// <summary>Health-status-change alert (Slack dropped, AI misconfigured, etc.) - distinct
    /// from the per-action toasts above, with no action/work item to deep-link to; clicking it
    /// opens the health details dialog instead.</summary>
    public void ShowHealthChangedToast(string title, string message)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            var builder = new AppNotificationBuilder()
                .AddArgument("openHealth", "1")
                .AddText(title)
                .AddText(message);

            AppNotificationManager.Default.Show(builder.BuildNotification());
        });
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "...";

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        if (args.Arguments.TryGetValue("actionId", out var rawAction) && Guid.TryParse(rawAction, out var actionId))
        {
            _dispatcherQueue.TryEnqueue(() => Activated?.Invoke(actionId));
            return;
        }

        if (args.Arguments.TryGetValue("workItemId", out var rawWorkItem) && Guid.TryParse(rawWorkItem, out var workItemId))
        {
            _dispatcherQueue.TryEnqueue(() => WorkItemActivated?.Invoke(workItemId));
            return;
        }

        if (args.Arguments.ContainsKey("openHealth"))
        {
            _dispatcherQueue.TryEnqueue(() => HealthActivated?.Invoke());
        }
    }

    public void Dispose()
    {
        _notifier.ActionStatusChanged -= OnActionStatusChanged;
        _notifier.WorkItemCommentAdded -= OnWorkItemCommentAdded;
        AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
    }
}
