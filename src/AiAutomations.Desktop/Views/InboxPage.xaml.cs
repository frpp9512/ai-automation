using AiAutomations.Desktop.Core;
using AiAutomations.Desktop.ViewModels;
using AiAutomations.Desktop.Views.Controls;
using AiAutomations.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AiAutomations.Desktop.Views;

public sealed partial class InboxPage : Page
{
    private readonly InboxViewModel _viewModel;
    private readonly TriageAppService _triageAppService;

    public InboxPage()
    {
        InitializeComponent();

        _triageAppService = App.Host.Services.GetRequiredService<TriageAppService>();
        _viewModel = new InboxViewModel(
            _triageAppService,
            App.Host.Services.GetRequiredService<DesktopRealtimeNotifier>(),
            DispatcherQueue);
        _viewModel.PendingActionsChanged += RenderCards;

        var today = DateTimeOffset.Now;
        BackfillFromPicker.Date = today.AddDays(-7);
        BackfillToPicker.Date = today;

        Loaded += async (_, _) => await _viewModel.RefreshAsync();
        Unloaded += (_, _) => _viewModel.Dispose();
    }

    private async void OnBackfill(object sender, RoutedEventArgs e)
    {
        var from = BackfillFromPicker.Date;
        var to = BackfillToPicker.Date;
        if (from is null || to is null)
        {
            return;
        }

        BackfillButton.IsEnabled = false;
        BackfillRunningPanel.Visibility = Visibility.Visible;
        BackfillResultBar.IsOpen = false;
        BackfillRunningText.Text = $"Searching Slack from {from.Value:MMM d} to {to.Value:MMM d}...";

        try
        {
            // End-of-day on the "to" date, so the selected day itself is fully included.
            var toInclusive = to.Value.Date.AddDays(1).AddTicks(-1);
            var toInclusiveOffset = new DateTimeOffset(toInclusive, to.Value.Offset);

            var result = await _triageAppService.BackfillAsync(from.Value, toInclusiveOffset);

            BackfillResultBar.Severity = InfoBarSeverity.Success;
            BackfillResultBar.Title = "Backfill complete";
            BackfillResultBar.Message = $"Found {result.MessagesFound} message(s) - ingested {result.NewMessages} new, {result.Skipped} already seen.";
            BackfillResultBar.IsOpen = true;

            await _viewModel.RefreshAsync();
        }
        catch (Exception ex)
        {
            BackfillResultBar.Severity = InfoBarSeverity.Error;
            BackfillResultBar.Title = "Backfill failed";
            BackfillResultBar.Message = ex.Message;
            BackfillResultBar.IsOpen = true;
        }
        finally
        {
            BackfillButton.IsEnabled = true;
            BackfillRunningPanel.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Opens the detail dialog for a specific action - used for the notification
    /// deep-link. If the action already got resolved before this fires (race with the user
    /// acting on it elsewhere), this is a silent no-op rather than an error.</summary>
    public async void ShowDetailFor(Guid actionId)
    {
        var action = _viewModel.PendingActions.FirstOrDefault(a => a.Id == actionId);
        MessageDecisionDto? decision;
        SlackMessageDto? message;

        if (action is not null)
        {
            decision = _viewModel.DecisionFor(action);
            message = _viewModel.MessageFor(action);
        }
        else
        {
            // Not pending (e.g. a Failed-action notification click) - look it up directly
            // rather than silently doing nothing.
            action = await _triageAppService.GetActionAsync(actionId);
            if (action is null)
            {
                return;
            }

            decision = await _triageAppService.GetDecisionAsync(action.DecisionId);
            message = decision is not null ? await _triageAppService.GetMessageAsync(decision.MessageId) : null;
        }

        var dialog = new Controls.ActionDetailDialog(action, decision, message) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }

    private void RenderCards()
    {
        LoadingRing.IsActive = _viewModel.IsLoading;

        CardsPanel.Children.Clear();

        if (_viewModel.PendingActions.Count == 0)
        {
            CardsPanel.Children.Add(EmptyText);
            EmptyText.Visibility = Visibility.Visible;
            return;
        }

        foreach (var action in _viewModel.PendingActions)
        {
            var card = new ActionCardControl();
            card.Populate(action, _viewModel.DecisionFor(action), _viewModel.MessageFor(action), _viewModel.Repositories);
            card.Approved += async (a, payload) => await _viewModel.ApproveAsync(a, payload);
            card.Rejected += async a => await _viewModel.RejectAsync(a);
            CardsPanel.Children.Add(card);
        }
    }
}
