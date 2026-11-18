using System.IO;
using System.Text.Json;

namespace DesktopMusicWidget;

/// <summary>
/// Centralized widget preferences. New user-facing settings should be added here,
/// not scattered across individual windows or controls.
/// </summary>
public sealed class WidgetSettings
{
    public int Version { get; set; } = 1;
    public string LastUsedView { get; set; } = "Compact";

    // Reserved for upcoming customization options.
    public string Theme { get; set; } = "Dark";
    public string AccentColor { get; set; } = "#A7A7A7";
    public double WindowOpacity { get; set; } = 1.0;
    public bool StartWithWindows { get; set; } = false;
}

public static class WidgetSettingsStore
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DesktopMusicWidget");

    private static readonly string FilePath = Path.Combine(DirectoryPath, "widget-settings.json");

    public static WidgetSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new WidgetSettings();

            var loaded = JsonSerializer.Deserialize<WidgetSettings>(File.ReadAllText(FilePath));
            return loaded ?? new WidgetSettings();
        }
        catch
        {
            // Invalid or old settings must never prevent the widget from starting.
            return new WidgetSettings();
        }
    }

    public static void Save(WidgetSettings settings)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Preferences are non-critical; playback remains available if saving fails.
        }
    }
}
