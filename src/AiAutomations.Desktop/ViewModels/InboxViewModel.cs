using AiAutomations.Desktop.Core;
using AiAutomations.Shared;
using Microsoft.UI.Dispatching;

namespace AiAutomations.Desktop.ViewModels;

/// <summary>
/// Plain view-model (no XAML data-binding) - InboxPage re-renders its card list imperatively
/// whenever this raises PendingActionsChanged, which keeps the WinUI3 side simple for Phase 1.
/// </summary>
public class InboxViewModel : IDisposable
{
    private readonly TriageAppService _triageAppService;
    private readonly DesktopRealtimeNotifier _notifier;
    private readonly DispatcherQueue _dispatcherQueue;

    private Dictionary<Guid, MessageDecisionDto> _decisionsById = new();
    private Dictionary<Guid, SlackMessageDto> _messagesById = new();

    public List<ActionItemDto> PendingActions { get; private set; } = [];
    public List<RepositoryProfileDto> Repositories { get; private set; } = [];
    public bool IsLoading { get; private set; }

    public event Action? PendingActionsChanged;

    public InboxViewModel(TriageAppService triageAppService, DesktopRealtimeNotifier notifier, DispatcherQueue dispatcherQueue)
    {
        _triageAppService = triageAppService;
        _notifier = notifier;
        _dispatcherQueue = dispatcherQueue;
        _notifier.ActionStatusChanged += OnActionStatusChanged;
    }

    private void OnActionStatusChanged(ActionItemDto _) =>
        _dispatcherQueue.TryEnqueue(async void () => await RefreshAsync());

    /// <summary>Unsubscribes from the app-lifetime notifier - call from the owning Page's
    /// Unloaded, since Frame.Navigate creates a new page (and a new InboxViewModel) every time
    /// you revisit this tab; without this, every past instance would keep refreshing forever.</summary>
    public void Dispose() => _notifier.ActionStatusChanged -= OnActionStatusChanged;

    public MessageDecisionDto? DecisionFor(ActionItemDto action) => _decisionsById.GetValueOrDefault(action.DecisionId);

    public SlackMessageDto? MessageFor(ActionItemDto action) =>
        DecisionFor(action) is { } decision ? _messagesById.GetValueOrDefault(decision.MessageId) : null;

    public async Task RefreshAsync()
    {
        IsLoading = true;
        PendingActionsChanged?.Invoke();

        try
        {
            var actionsTask = _triageAppService.GetPendingActionsAsync();
            var decisionsTask = _triageAppService.GetRecentDecisionsAsync(200);
            var messagesTask = _triageAppService.GetRecentMessagesAsync(200);
            var reposTask = _triageAppService.GetRepositoriesAsync();
            await Task.WhenAll(actionsTask, decisionsTask, messagesTask, reposTask);

            _decisionsById = (await decisionsTask).ToDictionary(d => d.Id);
            _messagesById = (await messagesTask).ToDictionary(m => m.Id);
            PendingActions = await actionsTask;
            Repositories = await reposTask;
        }
        finally
        {
            IsLoading = false;
            PendingActionsChanged?.Invoke();
        }
    }

    public async Task ApproveAsync(ActionItemDto action, string? editedPayload)
    {
        await _triageAppService.ApproveActionAsync(action.Id, editedPayload);
        await RefreshAsync();
    }

    public async Task RejectAsync(ActionItemDto action)
    {
        await _triageAppService.RejectActionAsync(action.Id);
        await RefreshAsync();
    }
}
