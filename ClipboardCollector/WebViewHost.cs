using System.IO;
using Microsoft.Web.WebView2.Core;

namespace ClipboardCollector;

/// <summary>
/// One WebView2 environment shared by the preview pane and the PDF export. Its data folder lives in
/// %LocalAppData% rather than next to the exe, where Velopack would wipe it on every update.
/// </summary>
internal static class WebViewHost
{
    private static readonly string UserDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClipboardCollector",
        "WebView2");

    private static Task<CoreWebView2Environment>? _environment;

    /// <exception cref="WebView2RuntimeNotFoundException">The Edge WebView2 runtime is not installed</exception>
    public static Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        // Retry on a later call if creation failed (e.g. the runtime was installed in the meantime)
        if (_environment is null || _environment.IsFaulted)
            _environment = CoreWebView2Environment.CreateAsync(userDataFolder: UserDataFolder);
        return _environment;
    }

    /// <summary>Renders the HTML in an invisible WebView2 and prints it to <paramref name="pdfPath"/>.</summary>
    /// <param name="parentWindow">Any window handle; the WebView2 is never shown</param>
    public static async Task ExportPdfAsync(string html, string pdfPath, IntPtr parentWindow)
    {
        var environment = await GetEnvironmentAsync();
        var controller = await environment.CreateCoreWebView2ControllerAsync(parentWindow);
        // Large collections can exceed NavigateToString's 2 MB limit, so go through a temp file
        var tempFile = Path.Combine(Path.GetTempPath(), $"ClipboardCollector_{Guid.NewGuid():N}.html");
        try
        {
            controller.IsVisible = false;
            var webView = controller.CoreWebView2;
            webView.Settings.IsScriptEnabled = false;

            await File.WriteAllTextAsync(tempFile, html);
            var loaded = new TaskCompletionSource<bool>();
            webView.NavigationCompleted += (_, e) => loaded.TrySetResult(e.IsSuccess);
            webView.Navigate(new Uri(tempFile).AbsoluteUri);
            if (!await loaded.Task)
                throw new IOException("The document could not be rendered.");

            var settings = environment.CreatePrintSettings();
            settings.ShouldPrintHeaderAndFooter = false;
            settings.ShouldPrintBackgrounds = true; // code block shading
            settings.MarginTop = settings.MarginBottom = 0.7; // inches
            settings.MarginLeft = settings.MarginRight = 0.65;

            if (!await webView.PrintToPdfAsync(pdfPath, settings))
                throw new IOException("WebView2 could not write the PDF.");
        }
        finally
        {
            controller.Close();
            try { File.Delete(tempFile); } catch (IOException) { }
        }
    }
}
