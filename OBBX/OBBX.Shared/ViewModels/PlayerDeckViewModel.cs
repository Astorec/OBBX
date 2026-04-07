using OBBX.Shared.Services;

namespace OBBX.Shared.ViewModels;

public class PlayerDeckProfileViewModel
{
    private readonly SettingsService _settingsService;
    private readonly CSVService _csvService;
    public PlayerDeckProfileViewModel(SettingsService settingsService, CSVService cSVService)
    {
        _settingsService = settingsService;
        _csvService = cSVService;
    }

    public async Task<Dictionary<string, DeckProfile>> LoadPlayerDeckProfilesAsync()
    {
        try
        {
            return _csvService.GetCachedProfiles();
        }
        catch (Exception ex)
        {
            // Handle exceptions (e.g., log error, show message to user)
            Console.Error.WriteLine($"Error loading player deck profiles: {ex.Message}");
            return new Dictionary<string, DeckProfile>();
        }
    }
    
    public async Task SaveProfileAsync(string username, DeckProfile profile)
    {
        try
        {
            await _csvService.UpdateKeyPairAsync(username, profile);
        }
        catch (Exception ex)
        {
            // Handle exceptions (e.g., log error, show message to user)
            Console.Error.WriteLine($"Error saving player deck profiles: {ex.Message}");
        }
    }
}