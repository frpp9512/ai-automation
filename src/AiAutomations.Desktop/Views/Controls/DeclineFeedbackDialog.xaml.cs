using Microsoft.UI.Xaml.Controls;

namespace AiAutomations.Desktop.Views.Controls;

/// <summary>
/// Review step for the "Reply" (decline-with-feedback) option - the drafted message is only
/// ever sent, and the item only ever declined, once the user explicitly clicks "Send & decline"
/// here.
/// </summary>
public sealed partial class DeclineFeedbackDialog : ContentDialog
{
    public DeclineFeedbackDialog(string draftText)
    {
        InitializeComponent();
        MessageBox.Text = draftText;
    }

    public string MessageText => MessageBox.Text;
}
