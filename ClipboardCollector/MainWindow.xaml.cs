using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Velopack;
using Velopack.Sources;

namespace ClipboardCollector;

public partial class MainWindow : Window
{
    public static readonly RoutedUICommand PasteEntry = new("Paste entry", nameof(PasteEntry), typeof(MainWindow));
    public static readonly RoutedUICommand ToggleCapture = new("Toggle auto-capture", nameof(ToggleCapture), typeof(MainWindow));
    public static readonly RoutedUICommand ToggleTopmost = new("Toggle always on top", nameof(ToggleTopmost), typeof(MainWindow));
    public static readonly RoutedUICommand TogglePreview = new("Toggle preview", nameof(TogglePreview), typeof(MainWindow));
    public static readonly RoutedUICommand ExportPdf = new("Export as PDF", nameof(ExportPdf), typeof(MainWindow));

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardOwner();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private const int WM_CLIPBOARDUPDATE = 0x031D;

    // Public GitHub repo whose releases BuildRelease.ps1 uploads to
    private const string RepoUrl = "https://github.com/kreaton/ClipboardCollector";

    private const string FileFilter = "Markdown Files (*.md)|*.md|Text Files (*.txt)|*.txt|All Files (*.*)|*.*";

    private readonly UpdateManager _updateManager = new(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly string _baseTitle = "ClipboardCollector";

    private HwndSource? _hwndSource;
    private string? _currentFile;
    private bool _isDirty;
    private bool _suppressDirty;
    private int _capturedCount;

    // Some apps (browsers' "copy" buttons in particular) raise several updates for one copy
    private string? _lastCaptured;
    private DateTime _lastCapturedAt;

    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private double _previewScrollY;
    private bool _previewFollowEnd = true;
    private string? _previewTempFile;

    public MainWindow()
    {
        InitializeComponent();

        // CurrentVersion is null when running from Visual Studio rather than an installed copy
        if (_updateManager.CurrentVersion is { } version)
            _baseTitle += $" {version}";

        Width = _settings.Width;
        Height = _settings.Height;
        if (_settings.Left is double left && _settings.Top is double top && IsOnScreen(left, top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        CaptureToggle.IsChecked = _settings.AutoCapture;
        TopmostToggle.IsChecked = _settings.AlwaysOnTop;
        UpdateCaptureUi();
        UpdateTitle();
        UpdateCounts();

        _previewTimer.Tick += async (_, _) =>
        {
            _previewTimer.Stop();
            await RenderPreviewAsync();
        };
    }

    private static bool IsOnScreen(double left, double top) =>
        left >= SystemParameters.VirtualScreenLeft - 50 &&
        top >= SystemParameters.VirtualScreenTop - 50 &&
        left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
        top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Only now, so the WebView2 has a window to live in
        PreviewToggle.IsChecked = _settings.ShowPreview;
        Editor.Focus();
    }

    #region Clipboard listener

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwndSource = (HwndSource)PresentationSource.FromVisual(this);
        _hwndSource.AddHook(WndProc);
        AddClipboardFormatListener(_hwndSource.Handle);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hwndSource != null)
        {
            RemoveClipboardFormatListener(_hwndSource.Handle);
            _hwndSource.RemoveHook(WndProc);
        }
        if (_previewTempFile != null)
            try { File.Delete(_previewTempFile); } catch (IOException) { }
        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // The listener stays registered; the toggle only decides whether updates are used
        if (msg == WM_CLIPBOARDUPDATE && CaptureToggle.IsChecked == true && !IsOwnClipboard())
        {
            var text = TryGetClipboardText();
            if (!string.IsNullOrWhiteSpace(text) &&
                !(text == _lastCaptured && DateTime.UtcNow - _lastCapturedAt < TimeSpan.FromSeconds(1)))
            {
                _lastCaptured = text;
                _lastCapturedAt = DateTime.UtcNow;
                AppendEntry(text);
            }
        }
        return IntPtr.Zero;
    }

    // Copying from the editor or the preview must not append the selection again. The preview
    // copies from a WebView2 child process, so also treat any copy made while one of our windows
    // (main window or a dialog) is in front as ours.
    private static bool IsOwnClipboard() =>
        IsOwnWindow(GetForegroundWindow()) || IsOwnWindow(GetClipboardOwner());

    private static bool IsOwnWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid == Environment.ProcessId;
    }

    private static string? TryGetClipboardText()
    {
        // Another process may still hold the clipboard open right after the update
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (COMException)
            {
                Thread.Sleep(30);
            }
        }
        return null;
    }

    #endregion

    #region Entries

    private void AppendEntry(string text)
    {
        var current = Editor.Text;
        var prefix = "";
        var trimmed = current.TrimEnd();
        if (trimmed.Length > 0)
        {
            // Separate entries with a Markdown rule. It needs a blank line above, or Markdown
            // would read the "---" as underlining the previous line into a heading.
            var trailing = current.Substring(current.TrimEnd('\r', '\n').Length);
            var newlines = trailing.Count(c => c == '\n');
            prefix = new string('\n', Math.Max(0, 2 - newlines)).Replace("\n", Environment.NewLine);
            if (!trimmed.EndsWith("---"))
                prefix += "---" + Environment.NewLine + Environment.NewLine;
        }

        // Normalise line endings so the TextBox and the saved file agree
        var normalised = text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine).TrimEnd();
        Editor.AppendText(prefix + normalised + Environment.NewLine);
        Editor.ScrollToEnd();

        _capturedCount++;
        UpdateCounts();
    }

    private void PasteEntry_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var text = TryGetClipboardText();
        if (string.IsNullOrWhiteSpace(text))
        {
            CaptureStatus.Text = "Nothing to paste: the clipboard holds no text";
            return;
        }
        AppendEntry(text);
    }

    private void Editor_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_suppressDirty && !_isDirty)
        {
            _isDirty = true;
            UpdateTitle();
        }
        UpdateCounts();

        if (PreviewToggle.IsChecked == true)
        {
            // Re-render once typing pauses rather than on every keystroke
            _previewTimer.Stop();
            _previewTimer.Start();
        }
    }

    private void SetText(string text, string? file)
    {
        _suppressDirty = true;
        Editor.Text = text;
        _suppressDirty = false;
        _currentFile = file;
        _isDirty = false;
        _capturedCount = 0;
        _lastCaptured = null;
        UpdateTitle();
        UpdateCounts();
        Editor.CaretIndex = Editor.Text.Length;
        Editor.ScrollToEnd();
    }

    #endregion

    #region Toggles

    private void CaptureToggle_Changed(object sender, RoutedEventArgs e)
    {
        _settings.AutoCapture = CaptureToggle.IsChecked == true;
        _settings.Save();
        UpdateCaptureUi();
    }

    private void ToggleCapture_Executed(object sender, ExecutedRoutedEventArgs e) =>
        CaptureToggle.IsChecked = CaptureToggle.IsChecked != true;

    private void UpdateCaptureUi()
    {
        var on = CaptureToggle.IsChecked == true;
        CaptureGlyph.Text = on ? "" : ""; // Pause / Play
        CaptureDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
            on ? "SystemFillColorSuccessBrush" : "TextFillColorTertiaryBrush");
        CaptureStatus.Text = on
            ? "Capturing: text you copy anywhere is added automatically"
            : "Paused: use Paste entry (Ctrl+Shift+V) to add the clipboard";
    }

    private void TopmostToggle_Changed(object sender, RoutedEventArgs e)
    {
        Topmost = TopmostToggle.IsChecked == true;
        _settings.AlwaysOnTop = Topmost;
        _settings.Save();
    }

    private void ToggleTopmost_Executed(object sender, ExecutedRoutedEventArgs e) =>
        TopmostToggle.IsChecked = TopmostToggle.IsChecked != true;


    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = MoreButton.ContextMenu;
        menu.PlacementTarget = MoreButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    #endregion

    #region Preview and PDF

    private async void PreviewToggle_Changed(object sender, RoutedEventArgs e)
    {
        var show = PreviewToggle.IsChecked == true;
        _settings.ShowPreview = show;
        _settings.Save();

        SplitterColumn.Width = show ? GridLength.Auto : new GridLength(0);
        PreviewColumn.Width = show ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        PreviewColumn.MinWidth = show ? 200 : 0;
        PreviewSplitter.Visibility = PreviewBorder.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        if (!show)
        {
            _previewTimer.Stop();
            return;
        }

        if (!await EnsurePreviewAsync())
        {
            PreviewToggle.IsChecked = false;
            return;
        }
        await RenderPreviewAsync();
    }

    private void TogglePreview_Executed(object sender, ExecutedRoutedEventArgs e) =>
        PreviewToggle.IsChecked = PreviewToggle.IsChecked != true;

    /// <returns>false if WebView2 is unavailable</returns>
    private async Task<bool> EnsurePreviewAsync()
    {
        if (PreviewView.CoreWebView2 != null)
            return true;

        try
        {
            await PreviewView.EnsureCoreWebView2Async(await WebViewHost.GetEnvironmentAsync());
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowWebView2Missing();
            return false;
        }

        var webView = PreviewView.CoreWebView2!;
        webView.Settings.AreDevToolsEnabled = false;
        webView.Settings.AreDefaultContextMenusEnabled = true; // keeps Copy
        webView.Settings.IsStatusBarEnabled = false;
        // Links open in the default browser instead of replacing the preview
        webView.NavigationStarting += (_, args) =>
        {
            if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
            {
                args.Cancel = true;
                Process.Start(new ProcessStartInfo(args.Uri) { UseShellExecute = true });
            }
        };
        webView.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            Process.Start(new ProcessStartInfo(args.Uri) { UseShellExecute = true });
        };
        webView.NavigationCompleted += async (_, _) =>
        {
            // Keep the reader's place across re-renders; follow new entries when at the end
            var script = _previewFollowEnd
                ? "window.scrollTo(0, document.body.scrollHeight)"
                : $"window.scrollTo(0, {_previewScrollY.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
            await webView.ExecuteScriptAsync(script);
        };
        return true;
    }

    private async Task RenderPreviewAsync()
    {
        var webView = PreviewView.CoreWebView2;
        if (webView == null)
            return;

        var scroll = await webView.ExecuteScriptAsync(
            "JSON.stringify([window.scrollY, window.innerHeight + window.scrollY >= document.body.scrollHeight - 4])");
        // ExecuteScriptAsync returns the JSON of the JSON string, so decode twice
        if (System.Text.Json.JsonSerializer.Deserialize<string>(scroll) is { } json &&
            System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement[]>(json) is [var y, var atEnd])
        {
            _previewScrollY = y.GetDouble();
            _previewFollowEnd = atEnd.GetBoolean() || Editor.CaretIndex >= Editor.Text.Length;
        }

        var html = MarkdownHtml.ToHtml(Editor.Text, DocumentTitle, forPrint: false);
        // NavigateToString is limited to 2 MB; huge collections go through a temp file
        if (html.Length < 1_000_000)
        {
            webView.NavigateToString(html);
        }
        else
        {
            _previewTempFile ??= Path.Combine(Path.GetTempPath(), $"ClipboardCollector_preview_{Environment.ProcessId}.html");
            await File.WriteAllTextAsync(_previewTempFile, html);
            webView.Navigate(new Uri(_previewTempFile).AbsoluteUri);
        }
    }

    private async void ExportPdf_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Editor.Text))
        {
            CaptureStatus.Text = "Nothing to export yet";
            return;
        }

        var sfd = new SaveFileDialog
        {
            Filter = "PDF Files (*.pdf)|*.pdf",
            Title = "Export as PDF",
            FileName = Path.GetFileNameWithoutExtension(_currentFile) ?? $"clipboard_{DateTime.Now:yyyy-MM-dd_HHmm}",
            InitialDirectory = _currentFile != null ? Path.GetDirectoryName(_currentFile) : _settings.LastSaveDirectory,
        };
        if (sfd.ShowDialog(this) != true)
            return;

        ExportPdfButton.IsEnabled = false;
        CaptureStatus.Text = "Exporting PDF…";
        try
        {
            var html = MarkdownHtml.ToHtml(Editor.Text, DocumentTitle, forPrint: true);
            await WebViewHost.ExportPdfAsync(html, sfd.FileName, _hwndSource!.Handle);
            CaptureStatus.Text = $"Exported {Path.GetFileName(sfd.FileName)} at {DateTime.Now:HH:mm}";
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowWebView2Missing();
            UpdateCaptureUi();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException)
        {
            MessageBox.Show(this, $"Could not export the PDF:\n{ex.Message}", "Export as PDF", MessageBoxButton.OK, MessageBoxImage.Error);
            UpdateCaptureUi();
        }
        finally
        {
            ExportPdfButton.IsEnabled = true;
        }
    }

    private string DocumentTitle =>
        _currentFile != null ? Path.GetFileNameWithoutExtension(_currentFile) : "Clipboard collection";

    private void ShowWebView2Missing() =>
        MessageBox.Show(this,
            "This needs the Microsoft Edge WebView2 Runtime, which is part of Windows 11 but appears to be missing.\n\n" +
            "Install it from https://go.microsoft.com/fwlink/p/?LinkId=2124703 and try again.",
            "WebView2 not found", MessageBoxButton.OK, MessageBoxImage.Warning);

    #endregion

    #region Files

    private void New_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (ConfirmDiscard())
            SetText("", null);
    }

    private void Open_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!ConfirmDiscard())
            return;

        var ofd = new OpenFileDialog
        {
            Filter = FileFilter,
            Title = "Open",
            InitialDirectory = _settings.LastSaveDirectory,
        };
        if (ofd.ShowDialog(this) != true)
            return;

        try
        {
            SetText(File.ReadAllText(ofd.FileName), ofd.FileName);
            RememberDirectory(ofd.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not open the file:\n{ex.Message}", "Open", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save_Executed(object sender, ExecutedRoutedEventArgs e) => Save(saveAs: false);
    private void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e) => Save(saveAs: true);
    private void SaveAsMenuItem_Click(object sender, RoutedEventArgs e) => Save(saveAs: true);

    /// <returns>true if the text was written</returns>
    private bool Save(bool saveAs)
    {
        var file = _currentFile;
        if (saveAs || file == null)
        {
            var sfd = new SaveFileDialog
            {
                Filter = FileFilter,
                Title = "Save",
                FileName = file != null ? Path.GetFileName(file) : $"clipboard_{DateTime.Now:yyyy-MM-dd_HHmm}",
                InitialDirectory = file != null ? Path.GetDirectoryName(file) : _settings.LastSaveDirectory,
            };
            if (sfd.ShowDialog(this) != true)
                return false;
            file = sfd.FileName;
        }

        try
        {
            File.WriteAllText(file, Editor.Text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not save the file:\n{ex.Message}", "Save", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        _currentFile = file;
        _isDirty = false;
        RememberDirectory(file);
        UpdateTitle();
        CaptureStatus.Text = $"Saved {Path.GetFileName(file)} at {DateTime.Now:HH:mm}";
        return true;
    }

    private void RememberDirectory(string file)
    {
        _settings.LastSaveDirectory = Path.GetDirectoryName(file)!;
        _settings.Save();
    }

    /// <returns>true if it is fine to throw away the current text</returns>
    private bool ConfirmDiscard()
    {
        if (!_isDirty || Editor.Text.Length == 0)
            return true;

        var name = _currentFile != null ? Path.GetFileName(_currentFile) : "this collection";
        var answer = MessageBox.Show(this, $"Save changes to {name}?", _baseTitle,
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer switch
        {
            MessageBoxResult.Yes => Save(saveAs: false),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }

        if (WindowState == WindowState.Normal)
        {
            _settings.Left = Left;
            _settings.Top = Top;
            _settings.Width = Width;
            _settings.Height = Height;
        }
        else
        {
            _settings.Left = RestoreBounds.Left;
            _settings.Top = RestoreBounds.Top;
            _settings.Width = RestoreBounds.Width;
            _settings.Height = RestoreBounds.Height;
        }
        _settings.Save();
    }

    #endregion

    private void UpdateTitle()
    {
        var name = _currentFile != null ? Path.GetFileName(_currentFile) : "Untitled";
        Title = $"{(_isDirty ? "● " : "")}{name} – {_baseTitle}";
    }

    private void UpdateCounts()
    {
        var text = Editor.Text;
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var captured = _capturedCount == 1 ? "1 entry" : $"{_capturedCount} entries";
        CountStatus.Text = $"{captured} added  ·  {words:N0} words  ·  {text.Length:N0} chars";
    }

    private async void UpdateMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!_updateManager.IsInstalled)
        {
            MessageBox.Show(this, "Updates are only available in the installed app.", "Update Check", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var newVersion = await _updateManager.CheckForUpdatesAsync();
            if (newVersion == null)
            {
                MessageBox.Show(this, "No updates available.", "Update Check", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(this,
                $"A new version ({newVersion.TargetFullRelease.Version}) is available. Do you want to download and install it now?",
                "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes || !ConfirmDiscard())
                return;

            _isDirty = false; // already confirmed; don't ask again while closing

            await _updateManager.DownloadUpdatesAsync(newVersion);

            // Install new version and restart app
            _updateManager.ApplyUpdatesAndRestart(newVersion);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Update failed: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
