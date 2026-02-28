using MudBlazor;
using OBBX.Shared.Models;
using OBBX.Shared.Services;

namespace OBBX.Shared.ViewModels;

public class TablesViewModel : IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly MatchStateService _matchState;
    private readonly ISnackbar _snackbar;
    private readonly IDialogService _dialogService;

    private AppSettings _settings;

    // --- State ---
    public List<TournamentMatch> Matches { get; private set; } = new();
    public string SearchTerm { get; set; } = "";
    public bool IsLoading { get; private set; }
    public bool IsSyncingStations { get; private set; }
    public bool TableSyncEnabled { get; private set; }
    public int TableCount { get; private set; } = 4;
    public int LiveFeedTableNumber { get; private set; } = 1;

    public event Action? OnChanged;

    public TablesViewModel(
        SettingsService settingsService,
        MatchStateService matchState,
        ISnackbar snackbar,
        IDialogService dialogService)
    {
        _settingsService = settingsService;
        _matchState = matchState;
        _snackbar = snackbar;
        _dialogService = dialogService;

        _matchState.MatchesUpdated += HandleUpdate;
        _matchState.TableAssignmentChanged += TableAssignmentChanged;
    }

    public async Task InitializeAsync()
    {
        await LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        _settings = await _settingsService.GetSettingsAsync();
        TableCount = _settings.Tables?.TableCount ?? 4;
        LiveFeedTableNumber = _settings.Tables?.LiveFeedTableNumber ?? 1;
        TableSyncEnabled = _settings.Tables?.UseChallongeForTables ?? false;
    }

    public async Task OnItemDroppedAsync(MudItemDropInfo<TournamentMatch> dropInfo)

    {

        if (dropInfo.Item == null) return;


        var match = dropInfo.Item;

        var zone = dropInfo.DropzoneIdentifier;


        if (zone == "available")

        {

            // Dropped back to available - clear table assignment

            if (match.HasTableAssigned)

            {

                await ClearTableAsync(match.MatchId);

            }

        }

        else if (zone.StartsWith("table-"))

        {
            if (int.TryParse(zone.Replace("table-", ""), out int tableNum))

            {

                await AssignTableAsync(match.MatchId, tableNum);

            }

        }

    }

    public async Task LoadMatchesAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        NotifyChanged();

        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            if (!string.IsNullOrEmpty(settings.Challonge?.Uri))
            {
                await _matchState.InitializeAsync(settings.Challonge.Uri);
                Matches = _matchState.GetMatches().ToList();
            }
        }
        catch (Exception ex)
        {
            _snackbar.Add($"Error loading: {ex.Message}", Severity.Error);
        }
        finally
        {
            IsLoading = false;
            NotifyChanged();
        }
    }

    public string GetZoneForMatch(TournamentMatch match)
    {
        if (!string.IsNullOrEmpty(SearchTerm))
        {
            var term = SearchTerm.ToLowerInvariant();
            bool found = (match.Player1Name?.ToLowerInvariant().Contains(term) ?? false) ||
                         (match.Player2Name?.ToLowerInvariant().Contains(term) ?? false) ||
                         (match.Identifier?.ToLowerInvariant().Contains(term) ?? false);
            if (!found) return "hidden";
        }
        return match.HasTableAssigned ? $"table-{match.TableNumber}" : "available";
    }

    public async Task HandleDropAsync(MudItemDropInfo<TournamentMatch> dropInfo)
    {
        if (dropInfo.Item == null) return;

        if (dropInfo.DropzoneIdentifier == "available")
        {
            await ClearTableAsync(dropInfo.Item.MatchId);
        }
        else if (dropInfo.DropzoneIdentifier.StartsWith("table-"))
        {
            if (int.TryParse(dropInfo.DropzoneIdentifier.Replace("table-", ""), out int tableNum))
            {
                await AssignTableAsync(dropInfo.Item.MatchId, tableNum);
            }
        }
    }

    public async Task AssignTableAsync(long matchId, int tableNumber)
    {
        try
        {
            var existing = Matches.FirstOrDefault(m => m.TableNumber == tableNumber && m.MatchId != matchId);
            if (existing != null) await _matchState.ClearTableAssignmentAsync(existing.MatchId);

            await _matchState.AssignTableAsync(matchId, tableNumber);
            Matches = _matchState.GetMatches().ToList();
            _snackbar.Add("Table assigned.", Severity.Success);
        }
        catch (Exception ex)
        {
            _snackbar.Add(ex.Message, Severity.Error);
        }
    }

    public async Task ClearTableAsync(long matchId)
    {
        await _matchState.ClearTableAssignmentAsync(matchId);
        Matches = _matchState.GetMatches().ToList();
        _snackbar.Add("Table cleared", Severity.Info);
    }

    public async Task RefreshMatchesAsync()
    {
        IsSyncingStations = true;
        NotifyChanged();

        try
        {
            if (_settings.Tables.UseChallongeForTables)
            {
                await _matchState.RefreshStationsAsync();
                Matches = _matchState.GetMatches().ToList();
                _snackbar.Add("Table assignments synced with Challonge stations", Severity.Success);
            }
            else
            {
                await _matchState.RefreshFromChallongeAsync();
                await _matchState.RefreshStationsAsync();
                Matches = _matchState.GetMatches().ToList();
                _snackbar.Add("Matches refreshed from Challonge", Severity.Success);
            }
        }
        catch (Exception ex)
        {
            _snackbar.Add($"Error refreshing matches: {ex.Message}", Severity.Error);
        }
        finally
        {
            IsSyncingStations = false;
            NotifyChanged();
        }
    }
    public async Task SyncStationsAsync()
    {
        IsSyncingStations = true;
        NotifyChanged();

        try
        {
            await _matchState.RefreshStationsAsync();
            Matches = _matchState.GetMatches().ToList();
            _snackbar.Add("Table assignments synced with Challonge stations", Severity.Success);
        }
        catch (Exception ex)
        {
            _snackbar.Add($"Error syncing stations: {ex.Message}", Severity.Error);
        }
        finally
        {
            IsSyncingStations = false;
            NotifyChanged();
        }
    }
    public async Task OpenTableSettingsAsync()
    {
        var dialog = await _dialogService.ShowAsync<OBBX.Shared.Pages.Popups.TableSettings>("Table Settings");
        var result = await dialog.Result;
        if (!result.Canceled) await LoadSettingsAsync();
        NotifyChanged();
    }

    private void HandleUpdate(object? s, EventArgs e)
    {
        Matches = _matchState.GetMatches().ToList();
        NotifyChanged();
    }

    private void TableAssignmentChanged(object? s, EventArgs e)
    {
        Matches = _matchState.GetMatches().ToList();
        NotifyChanged();
    }

    public Color GetStateColor(TournamentMatchState state)

    {

        return state switch

        {

            TournamentMatchState.Open => Color.Success,

            TournamentMatchState.InProgress => Color.Warning,

            TournamentMatchState.Complete => Color.Default,

            _ => Color.Secondary

        };

    }

    private void NotifyChanged() => OnChanged?.Invoke();

    public void Dispose()
    {
        _matchState.MatchesUpdated -= HandleUpdate;
        _matchState.TableAssignmentChanged -= HandleUpdate;
    }
}