using CsvHelper;
using CsvHelper.Configuration;
using OBBX.Shared.Models;
using OBBX.Shared.Services;
using System.Globalization;
using System.IO;

public class CSVService
{   
    private readonly SettingsService _settingsService;
    private string? _csvImportPath;
    private Dictionary<string, DeckProfile> _cachedProfiles = new Dictionary<string, DeckProfile>();
    public CSVService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _cachedProfiles = new Dictionary<string, DeckProfile>();
    }

    // Import CSV file into PlayerDeckProfile list from a path on disk
    private async Task<Dictionary<string, DeckProfile>> ImportCSVAsync()
    {
        if (string.IsNullOrEmpty(_csvImportPath) || !File.Exists(_csvImportPath))
        {
            throw new FileNotFoundException("CSV import path is not set or file does not exist.");
        }

        using var reader = new StreamReader(_csvImportPath);
        return await ImportCSVAsync(reader);
    }

    // Import CSV file into PlayerDeckProfile list from a stream (browser upload)
    private async Task<Dictionary<string, DeckProfile>> ImportCSVAsync(Stream stream)
    {
        using var reader = new StreamReader(stream);
        return await ImportCSVAsync(reader);
    }

    // Common parse logic for a TextReader
private static async Task<Dictionary<string, DeckProfile>> ImportCSVAsync(TextReader reader)
{
    var profiles = new Dictionary<string, DeckProfile>();

    var config = new CsvConfiguration(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = true,
        MissingFieldFound = null,
        HeaderValidated = null
    };

    using var csv = new CsvReader(reader, config);

    while (await csv.ReadAsync())
    {
        // GetField(0) is Username, (1) is Bey01, etc.
        var username = csv.GetField(0)?.Trim() ?? string.Empty;

        // Skip empty usernames or header row if it wasn't caught
        if (string.IsNullOrEmpty(username) || username == "WBO_Username") continue;

        var profile = new DeckProfile
        {
            // CsvHelper correctly extracts "Wizard rod, 1-60 hexa" as one field
            Bey01 = csv.GetField(1)?.Trim() ?? string.Empty,
            Bey02 = csv.GetField(2)?.Trim() ?? string.Empty,
            Bey03 = csv.GetField(3)?.Trim() ?? string.Empty,
            Bey04 = csv.GetField(4)?.Trim() ?? string.Empty,
            Bey05 = csv.GetField(5)?.Trim() ?? string.Empty
        };

        profiles[username] = profile;
    }

    return profiles;
}

    public async Task UpdateImportPathAsync(string newPath)
    {
        _csvImportPath = newPath;
        var settings = await _settingsService.LoadAsync();
        if (settings.CsvImport == null) settings.CsvImport = new CsvImportSettings();
        settings.CsvImport.ImportPath = newPath;
        settings.CsvImport.IsBrowserUpload = false;
        settings.CsvImport.BrowserFileName = string.Empty;
        await _settingsService.SaveAsync(settings);
        _cachedProfiles = await ImportCSVAsync();
    }

    public async Task UpdateImportFromStreamAsync(Stream stream, string sourceName)
    {
        // For web, we cannot store the client-side file path, so mark as browser upload.
        _csvImportPath = string.Empty;

        var settings = await _settingsService.LoadAsync();
        if (settings.CsvImport == null) settings.CsvImport = new CsvImportSettings();
        settings.CsvImport.ImportPath = string.Empty;
        settings.CsvImport.IsBrowserUpload = true;
        settings.CsvImport.BrowserFileName = sourceName;
        await _settingsService.SaveAsync(settings);

        _cachedProfiles = await ImportCSVAsync(stream);
    }

    public Dictionary<string, DeckProfile> GetCachedProfiles()
    {
        return _cachedProfiles;
    }

    public async Task InitializeAsync()
    {
        var settings = await _settingsService.LoadAsync();

        _csvImportPath = settings.CsvImport?.ImportPath ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(_csvImportPath) && File.Exists(_csvImportPath))
        {
            try
            {
                _cachedProfiles = await ImportCSVAsync();
            }
            catch
            {
                _cachedProfiles = new Dictionary<string, DeckProfile>();
            }
        }
        else
        {
            _cachedProfiles = new Dictionary<string, DeckProfile>();
        }
    }

    public async Task ReloadCsvAsync(string? path = null)
    {
        var importPath = path ?? _csvImportPath;
        if (string.IsNullOrWhiteSpace(importPath) || !File.Exists(importPath))
        {
            return;
        }

        _csvImportPath = importPath;
        _cachedProfiles = await ImportCSVAsync();
    }

    public async Task UpdateKeyPairAsync(string username, DeckProfile profile)
    {
        _cachedProfiles[username] = profile;
        await SaveCSVAsync(_cachedProfiles);
    }

    public async Task SaveCSVAsync(Dictionary<string, DeckProfile> profiles)
    {
        if (string.IsNullOrEmpty(_csvImportPath))
        {
            throw new InvalidOperationException("CSV import path is not set.");
        }

        using var writer = new StreamWriter(_csvImportPath);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true
        };
        using var csv = new CsvWriter(writer, config);

        // Write header
        csv.WriteField("WBO_Username");
        csv.WriteField("Bey01");
        csv.WriteField("Bey02");
        csv.WriteField("Bey03");
        csv.WriteField("Bey04");
        csv.WriteField("Bey05");
        await csv.NextRecordAsync();

        // Write records
        foreach (var kvp in profiles)
        {
            csv.WriteField(kvp.Key); // Username
            csv.WriteField(kvp.Value.Bey01);
            csv.WriteField(kvp.Value.Bey02);
            csv.WriteField(kvp.Value.Bey03);
            csv.WriteField(kvp.Value.Bey04);
            csv.WriteField(kvp.Value.Bey05);
            await csv.NextRecordAsync();
        }

        await writer.FlushAsync();

        // Update cache after saving
        _cachedProfiles = new Dictionary<string, DeckProfile>(profiles);
    }
}