using AiAutomations.Shared;
using Microsoft.UI.Xaml.Controls;

namespace AiAutomations.Desktop.Views.Controls;

public sealed partial class RepositoryEditDialog : ContentDialog
{
    public RepositoryEditDialog(RepositoryProfileDto repo)
    {
        InitializeComponent();
        NameBox.Text = repo.Name;
        PathBox.Text = repo.Path;
        KeywordsBox.Text = repo.Keywords ?? string.Empty;

        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    public string RepoName => NameBox.Text;
    public string RepoPath => PathBox.Text;
    public string? RepoKeywords => string.IsNullOrWhiteSpace(KeywordsBox.Text) ? null : KeywordsBox.Text;

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(PathBox.Text))
        {
            args.Cancel = true;
        }
    }
}
