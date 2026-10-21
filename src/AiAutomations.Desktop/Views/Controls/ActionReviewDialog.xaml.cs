using AiAutomations.Core.Models;
using AiAutomations.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AiAutomations.Desktop.Views.Controls;

/// <summary>
/// "Check first" approval step - a pre-filled, still-editable view of the action's details,
/// shown before the user commits to Approve or Discard (as opposed to the inline card, which
/// approves/rejects immediately).
/// </summary>
public sealed partial class ActionReviewDialog : ContentDialog
{
    private const string NoneRepoOption = "(none - not repo specific)";

    private readonly bool _repoApplies;

    public ActionReviewDialog(ActionItemDto action, MessageDecisionDto? decision, string currentPayloadText,
        string? selectedRepoName, IReadOnlyList<RepositoryProfileDto> repositories)
    {
        InitializeComponent();

        SummaryText.Text = decision?.Summary ?? "(no summary)";
        PayloadBox.Text = currentPayloadText;

        _repoApplies = action.Type is ActionType.Ticket or ActionType.TaskBrief;
        if (_repoApplies)
        {
            RepoPicker.Visibility = Visibility.Visible;

            var items = new List<string> { NoneRepoOption };
            items.AddRange(repositories.Select(r => r.Name));
            RepoPicker.ItemsSource = items;
            RepoPicker.SelectedItem = repositories.FirstOrDefault(r => r.Name == selectedRepoName)?.Name ?? NoneRepoOption;
        }
    }

    public string PayloadText => PayloadBox.Text;

    public string? SelectedRepoName
    {
        get
        {
            if (!_repoApplies || RepoPicker.SelectedItem is not string selected || selected == NoneRepoOption)
            {
                return null;
            }

            return selected;
        }
    }
}
