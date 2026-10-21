using AiAutomations.Desktop.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel;

namespace AiAutomations.Desktop.Views;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();

        VersionText.Text = TryGetPackageVersion() is { } version ? $"Version {version}" : "Development build";
        ContactText.Text = "frpp9512@outlook.com";
        DataPathText.Text = $"Local data: {DesktopConfiguration.AppDataDirectory}";

        // An explicit ms-appx:// URI set in code is more robust than a bare relative XAML
        // Source string, which was silently failing to resolve here (reserved its layout space
        // via the Image's explicit Width/Height, but never actually painted anything).
        try
        {
            AppIconImage.Source = new BitmapImage(new Uri("ms-appx:///Assets/Square150x150Logo.png"));
        }
        catch
        {
            // Best-effort - the rest of the page is still useful without the icon.
        }
    }

    private static string? TryGetPackageVersion()
    {
        try
        {
            var v = Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch
        {
            // Unpackaged dev run (e.g. the verification harness) has no package identity.
            return null;
        }
    }
}
