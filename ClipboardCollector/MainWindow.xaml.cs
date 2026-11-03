using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

using Microsoft.Win32;
using Velopack;
using Velopack.Sources;

namespace ClipboardCollector;

public partial class MainWindow : Window
{
    public static readonly RoutedUICommand PasteEntry = new("Paste entry", nameof(PasteEntry), typeof(MainWindow));
    public static readonly RoutedUICommand ToggleCapture = new("Toggle auto-capture", nameof(ToggleCapture), typeof(MainWindow));
    public static readonly RoutedUICommand ToggleTopmost = new("Toggle always on top", nameof(ToggleTopmost), typeof(MainWindow));

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardOwner();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

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
        SeparatorMenuItem.IsChecked = _settings.InsertSeparator;
        UpdateCaptureUi();
        UpdateTopmostUi();
        UpdateTitle();
        UpdateCounts();
    }

    private static bool IsOnScreen(double left, double top) =>
        left >= SystemParameters.VirtualScreenLeft - 50 &&
        top >= SystemParameters.VirtualScreenTop - 50 &&
        left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
        top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100;

    private void Window_Loaded(object sender, RoutedEventArgs e) => Editor.Focus();

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

    // Copying from the editor itself must not append the selection again
    private static bool IsOwnClipboard()
    {
        var owner = GetClipboardOwner();
        if (owner == IntPtr.Zero)
            return false;
        GetWindowThreadProcessId(owner, out var pid);
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
        if (current.Length > 0)
        {
            // Make sure the new entry starts after a blank line (and a rule, if wanted)
            var trimmed = current.TrimEnd('\r', '\n');
            var trailing = current.Substring(trimmed.Length);
            var newlines = trailing.Count(c => c == '\n');
            prefix = new string('\n', Math.Max(0, 2 - newlines)).Replace("\n", Environment.NewLine);
            if (_settings.InsertSeparator)
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
        UpdateTopmostUi();
    }

    private void ToggleTopmost_Executed(object sender, ExecutedRoutedEventArgs e) =>
        TopmostToggle.IsChecked = TopmostToggle.IsChecked != true;

    private void UpdateTopmostUi() =>
        TopmostGlyph.Text = TopmostToggle.IsChecked == true ? "" : ""; // Pinned / Pin

    private void SeparatorMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.InsertSeparator = SeparatorMenuItem.IsChecked;
        _settings.Save();
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = MoreButton.ContextMenu;
        menu.PlacementTarget = MoreButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

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
