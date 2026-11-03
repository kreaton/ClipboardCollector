using System.IO;
using System.Text.Json;

namespace ClipboardCollector;

/// <summary>
/// Small JSON settings file in %LocalAppData%. Kept outside the app folder so it survives
/// Velopack updates.
/// </summary>
internal sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClipboardCollector",
        "settings.json");

    public string LastSaveDirectory { get; set; } =
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public bool AutoCapture { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    public bool InsertSeparator { get; set; }

    // Window placement; null means "let Windows decide"
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 820;
    public double Height { get; set; } = 640;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings: fall back to defaults
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth bothering the user about
        }
    }
}
