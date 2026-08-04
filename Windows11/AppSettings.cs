using System.Text.Json;

namespace PixelColorClicker;

internal sealed class AppSettings
{
    internal const int DefaultCountdownSeconds = 3;
    internal const int DefaultHotkey = (int)Keys.F8;

    public int Version { get; set; } = 1;
    public int? MonitorX { get; set; }
    public int? MonitorY { get; set; }
    public int SelectedMode { get; set; }
    public int Tolerance { get; set; } = 10;
    public int ClickLocation { get; set; }
    public int RegionSize { get; set; } = 1;
    public int ChangeDelayMilliseconds { get; set; }
    public int ChangeClickCount { get; set; } = 1;
    public int ChangeIntervalMilliseconds { get; set; } = 100;
    public int CountdownSeconds { get; set; } = DefaultCountdownSeconds;
    public int StartHotkey { get; set; } = DefaultHotkey;
    public List<ColorTargetSetting> TargetColors { get; set; } =
    [
        new ColorTargetSetting()
    ];
}

internal sealed class ColorTargetSetting
{
    public string Color { get; set; } = "#66D169";
    public int DelayMilliseconds { get; set; }
    public int ClickCount { get; set; } = 1;
    public int IntervalMilliseconds { get; set; } = 100;
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    internal static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    internal static bool Save(AppSettings settings)
    {
        try
        {
            string temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, SettingsPath, true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
