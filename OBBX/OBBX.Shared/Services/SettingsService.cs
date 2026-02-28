using System.Xml.Schema;
using System.Text.Json;
using OBBX.Shared.Models;

namespace OBBX.Shared.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private AppSettings? _cachedSettings;
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true
    };

    public SettingsService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OBBX");

        // Creates Directory @ %APPDATA%/OBBX if it doesn't exist
        Directory.CreateDirectory(appDataPath);
        _settingsPath = Path.Combine(appDataPath, "settings.json");
    }

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(_settingsPath))
        {
            var defaultSettings = new AppSettings();
            await SaveAsync(defaultSettings);
            return defaultSettings;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);

            // Return deserialized settings, or a new instance if deserialization results in null
            return settings ?? new AppSettings();
        }
        catch(Exception ex)
        {
            Console.WriteLine($"Error loading settings: {ex.Message}");
            return new AppSettings();
        }
    }

 public async Task SaveAsync(AppSettings settings)
    {
        try 
        {
            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            await File.WriteAllTextAsync(_settingsPath, json);
            
            // Update the cache immediately so other services get the new data
            _cachedSettings = settings; 
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving settings: {ex.Message}");
            throw; 
        }
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        if (_cachedSettings != null)
        {
            return _cachedSettings;
        }

        _cachedSettings = await LoadAsync();
        return _cachedSettings;
    }

    // Force reload from file
    private async Task<AppSettings> ReloadSettingsAsync()
    {
        _cachedSettings = await LoadAsync();
        return _cachedSettings;
    }
}