using Microsoft.UI.Xaml.Controls;

namespace AiAutomations.Desktop.Views.Controls;

/// <summary>
/// Review step for the optional post-approval Slack acknowledgment - never sent without the
/// user explicitly clicking "Send to Slack" here.
/// </summary>
public sealed partial class AcknowledgmentDialog : ContentDialog
{
    public AcknowledgmentDialog(string draftText)
    {
        InitializeComponent();
        MessageBox.Text = draftText;
    }

    public string MessageText => MessageBox.Text;
}
