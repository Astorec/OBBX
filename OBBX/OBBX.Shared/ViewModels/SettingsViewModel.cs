#if ANDROID || IOS || MACCATALYST || WINDOWS || TIZEN
using Microsoft.Maui.Storage;
#endif
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using OBBX.Shared.Models;
using OBBX.Shared.Services;
using System.IO;

namespace OBBX.Shared.ViewModels;

public class SettingsViewModel
{
    private readonly SettingsService _settingsService;
    private readonly MatchStateService _matchState;
    private readonly ISnackbar _snackbar;
    private readonly CSVService _csvService;

    // --- State ---
    public AppSettings Settings { get; private set; } = new();
    private string? _originalChallongeUri;
    public bool IsSaving { get; private set; }

    // --- UI Toggles (Password/API Key) ---
    public bool IsShowPassword { get; private set; }
    public InputType PasswordInput => IsShowPassword ? InputType.Text : InputType.Password;
    public string PasswordInputIcon => IsShowPassword ? Icons.Material.Filled.Visibility : Icons.Material.Filled.VisibilityOff;

    public bool IsShowApiKey { get; private set; }
    public InputType ApiKeyInput => IsShowApiKey ? InputType.Text : InputType.Password;
    public string ApiKeyInputIcon => IsShowApiKey ? Icons.Material.Filled.Visibility : Icons.Material.Filled.VisibilityOff;

    public string CsvImportPath
    {
        get => Settings.CsvImport?.ImportPath ?? string.Empty;
        set
        {
            if (Settings.CsvImport != null)
            {
                Settings.CsvImport.ImportPath = value;
                Settings.CsvImport.IsBrowserUpload = false;
                Settings.CsvImport.BrowserFileName = string.Empty;
                NotifyChanged();
            }
        }
    }

    public string CsvImportDisplay
    {
        get
        {
            if (Settings.CsvImport?.IsBrowserUpload == true)
            {
                return $"Uploaded: {Settings.CsvImport.BrowserFileName}";
            }
            return CsvImportPath;
        }
        private set
        {

        }
    }

    public event Action? OnChanged;

    public SettingsViewModel(SettingsService settingsService, MatchStateService matchState, CSVService cSVService, ISnackbar snackbar)
    {
        _settingsService = settingsService;
        _matchState = matchState;
        _csvService = cSVService;
        _snackbar = snackbar;
    }

    public async Task InitializeAsync()
    {
        Settings = await _settingsService.GetSettingsAsync();
        _originalChallongeUri = Settings.Challonge?.Uri;


        NotifyChanged();
    }

    public void TogglePasswordVisibility()
    {
        IsShowPassword = !IsShowPassword;
        NotifyChanged();
    }

    public void ToggleApiKeyVisibility()
    {
        IsShowApiKey = !IsShowApiKey;
        NotifyChanged();
    }

    public async Task SaveSettingsAsync()
    {
        if (IsSaving) return;

        IsSaving = true;
        NotifyChanged();

        var originalCsvPath = Settings.CsvImport?.ImportPath;
        var originalCsvBrowser = Settings.CsvImport?.IsBrowserUpload == true;

        try
        {
            await _settingsService.SaveAsync(Settings);

            // if path is set and non-browser, refresh the CSV import cache so changes are picked up.
            var newCsvPath = Settings.CsvImport?.ImportPath;
            var newCsvBrowser = Settings.CsvImport?.IsBrowserUpload == true;

            if (!newCsvBrowser && !string.IsNullOrWhiteSpace(newCsvPath))
            {
                await _csvService.ReloadCsvAsync(newCsvPath);
            }
            var newUri = Settings.Challonge?.Uri;
            if (!string.Equals(newUri, _originalChallongeUri, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(newUri))
            {
                await _matchState.ResetForNewTournamentAsync(newUri);
                _originalChallongeUri = newUri;
            }

            if (!string.Equals(originalCsvPath, newCsvPath, StringComparison.OrdinalIgnoreCase) || (originalCsvBrowser != newCsvBrowser))
            {
                _snackbar.Add("CSV path updated and reloaded.", Severity.Info);
            }
            else
            {
                _snackbar.Add("Settings Saved Successfully", Severity.Success);
            }
        }
        catch (Exception ex)
        {
            _snackbar.Add($"Error Saving Settings: {ex.Message}", Severity.Error);
        }
        finally
        {
            IsSaving = false;
            NotifyChanged();
        }
    }

    public async Task ImportCsvFromBrowser(InputFileChangeEventArgs args)
    {
        try
        {
            if (args.File == null)
            {
                return;
            }

            if (!args.File.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                _snackbar.Add("Please select a .csv file.", Severity.Warning);
                return;
            }

            await using var stream = args.File.OpenReadStream(10 * 1024 * 1024); // 10 MB cap
            await _csvService.UpdateImportFromStreamAsync(stream, args.File.Name);

            // Clear path (browser path is not reloadable). Keep only a user-friendly marker.
            CsvImportPath = string.Empty;
            if (Settings.CsvImport != null)
            {
                Settings.CsvImport.IsBrowserUpload = true;
                Settings.CsvImport.BrowserFileName = args.File.Name;
            }

            var uploadFolder = Path.Combine(
             Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OBBX");
            if (!Directory.Exists(uploadFolder)) Directory.CreateDirectory(uploadFolder);

            var filePath = Path.Combine(uploadFolder, args.File.Name);
            await using var fileStream = new FileStream(filePath, FileMode.Create);
            await args.File.OpenReadStream(10 * 1024 * 1024).CopyToAsync(fileStream);

            Settings.CsvImport.ImportPath = filePath;
            await _settingsService.SaveAsync(Settings);
            _snackbar.Add($"CSV file loaded: {args.File.Name}", Severity.Success);
            NotifyChanged();
        }
        catch (Exception ex)
        {
            _snackbar.Add($"Failed to import CSV: {ex.Message}", Severity.Error);
        }
    }

    public async Task ClearCsvImportAsync()
    {
        if (this.Settings.CsvImport != null)
        {
            this.Settings.CsvImport.ImportPath = string.Empty;
            this.Settings.CsvImport.IsBrowserUpload = false;
        }

        this.CsvImportDisplay = string.Empty;

        NotifyChanged();
    }
    private void NotifyChanged() => OnChanged?.Invoke();
}