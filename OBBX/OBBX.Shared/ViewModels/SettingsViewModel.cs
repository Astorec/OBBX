using MudBlazor;
using OBBX.Shared.Models;
using OBBX.Shared.Services;

namespace OBBX.Shared.ViewModels;

public class SettingsViewModel
{
    private readonly SettingsService _settingsService;
    private readonly MatchStateService _matchState;
    private readonly ISnackbar _snackbar;

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

    public event Action? OnChanged;

    public SettingsViewModel(SettingsService settingsService, MatchStateService matchState, ISnackbar snackbar)
    {
        _settingsService = settingsService;
        _matchState = matchState;
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

        try
        {
            await _settingsService.SaveAsync(Settings);

            // Logic: If the URL changed, we need to flush the old match data
            var newUri = Settings.Challonge?.Uri;
            if (!string.Equals(newUri, _originalChallongeUri, StringComparison.OrdinalIgnoreCase) 
                && !string.IsNullOrWhiteSpace(newUri))
            {
                await _matchState.ResetForNewTournamentAsync(newUri);
                _originalChallongeUri = newUri;
            }

            _snackbar.Add("Settings Saved Successfully", Severity.Success);
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

    private void NotifyChanged() => OnChanged?.Invoke();
}