using System;
using System.IO;
using System.Text.Json;
using Microsoft.UI.Xaml;

namespace Varys;

/// <summary>
/// Small persisted application settings. The app is unpackaged, so ApplicationData isn't
/// available — settings live in a JSON file under %LOCALAPPDATA%\Varys.
/// </summary>
public static class AppSettings
{
    private sealed class Data
    {
        public string Theme { get; set; } = "System";   // System | Light | Dark
        public string Language { get; set; } = "en";     // en | sv
    }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Varys", "settings.json");

    private static Data _data = new();

    static AppSettings() => Load();

    /// <summary>App theme: "System", "Light", or "Dark".</summary>
    public static string Theme
    {
        get => _data.Theme;
        set { _data.Theme = value; Save(); }
    }

    /// <summary>Default transcription language: "en" or "sv".</summary>
    public static string Language
    {
        get => _data.Language;
        set { _data.Language = value; Save(); }
    }

    /// <summary>The theme as an <see cref="ElementTheme"/> (System → Default).</summary>
    public static ElementTheme ElementTheme => _data.Theme switch
    {
        "Light" => ElementTheme.Light,
        "Dark" => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    private static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                _data = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data();
        }
        catch
        {
            _data = new Data();
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best-effort */ }
    }
}
