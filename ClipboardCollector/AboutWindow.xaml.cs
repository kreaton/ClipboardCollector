using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace ClipboardCollector;

public partial class AboutWindow : Window
{
    public AboutWindow(string repoUrl)
    {
        InitializeComponent();

        // An .ico holds several sizes; take the largest so the icon stays sharp at 64 px
        var decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/app.ico"),
            BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        AppIcon.Source = decoder.Frames.MaxBy(f => f.PixelWidth);

        VersionText.Text = BuildInfo.Display;
        CreditText.Text = BuildInfo.Copyright;
        RepoLink.NavigateUri = new Uri(repoUrl);
        RepoLinkText.Text = repoUrl.Replace("https://", "");
    }

    private void RepoLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
