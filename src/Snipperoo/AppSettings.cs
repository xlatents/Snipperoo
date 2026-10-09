using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snipperoo;

/// <summary>Discord upload tiers; clips are encoded to fit the tier's limit.</summary>
internal enum DiscordPlan { Free, NitroBasic, Nitro }

/// <summary>User settings, stored as JSON in %APPDATA%\Snipperoo. Missing keys fall back to the defaults below.</summary>
internal sealed class AppSettings
{
    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Snipperoo");

    public static string FilePath => Path.Combine(DataFolder, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Without this, '+' in hotkeys is written as +.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>False until the first-run setup has been finished.</summary>
    public bool SetupComplete { get; set; }

    public DiscordPlan Plan { get; set; } = DiscordPlan.Free;
    public string RecordHotkey { get; set; } = "Alt+Shift+G";
    public string ScreenshotHotkey { get; set; } = "Alt+Shift+S";
    public int FrameRate { get; set; } = 60;
    public bool RecordSystemAudio { get; set; } = true;
    public bool ShowCursor { get; set; } = true;
    public string OutputFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Snipperoo");
    public string FfmpegPath { get; set; } = "";

    [JsonIgnore]
    public string ScreenshotFolder => Path.Combine(OutputFolder, "Screenshots");

    [JsonIgnore]
    public long MaxFileSizeBytes => UploadLimitBytes(Plan);

    // Decimal megabytes: smaller than MiB, so a clip fits whichever way Discord counts.
    public static long UploadLimitBytes(DiscordPlan plan) => plan switch
    {
        DiscordPlan.Nitro => 500_000_000,
        DiscordPlan.NitroBasic => 50_000_000,
        _ => 10_000_000,
    };

    /// <summary>Loads settings, or defaults (with <see cref="SetupComplete"/> false) when there are none.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new();
        }
        catch (Exception ex)
        {
            Log.Error("Could not read settings, using defaults", ex);
        }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (IOException ex)
        {
            Log.Error("Could not save settings", ex);
        }
    }
}
