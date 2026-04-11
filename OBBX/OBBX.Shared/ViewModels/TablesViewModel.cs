using MudBlazor;
using OBBX.Shared.Models;
using OBBX.Shared.Services;

namespace OBBX.Shared.ViewModels;

public class TablesViewModel : IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly MatchStateService _matchState;
    private readonly OBSWebSocketService _obsService;
    private readonly ISnackbar _snackbar;
    private readonly IDialogService _dialogService;

    private AppSettings _settings;

    // --- State ---
    public List<TournamentMatch> Matches { get; private set; } = new();
    private string _searchTerm = "";
    private string _lastSearchTerm = "";
    private HashSet<long>? _filteredMatchIds;

    public string SearchTerm
    {
        get => _searchTerm;
        set
        {
            if (_searchTerm == value)
                return;
            _searchTerm = value;
            UpdateSearch(value);
        }
    }

    public bool IsLoading { get; private set; }
    public bool IsSyncingStations { get; private set; }
    public bool TableSyncEnabled { get; private set; }

    private void UpdateSearch(string term)
    {
        string normalized = term?.Trim() ?? string.Empty;
        if (_lastSearchTerm == normalized)
            return;

        _lastSearchTerm = normalized;

        if (string.IsNullOrWhiteSpace(normalized))
        {
            _filteredMatchIds = null;
        }
        else
        {
            _filteredMatchIds = Matches
                .Where(m => MatchesSearch(m, normalized))
                .Select(m => m.MatchId)
                .ToHashSet();
        }

        NotifyChanged();
    }

    private static bool MatchesSearch(TournamentMatch match, string searchTerm)
    {
        return (match.Player1Name?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false)
            || (match.Player2Name?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false)
            || (match.Identifier?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false);
    }
    public int TableCount { get; private set; } = 4;
    public int LiveFeedTableNumber { get; private set; } = 1;

    public event Action? OnChanged;

    public TablesViewModel(
        SettingsService settingsService,
        MatchStateService matchState,
        OBSWebSocketService obsService,
        ISnackbar snackbar,
        IDialogService dialogService)
    {
        _settingsService = settingsService;
        _matchState = matchState;
        _obsService = obsService;
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
                UpdateSearch(_searchTerm);
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
        if (!string.IsNullOrWhiteSpace(SearchTerm) && _filteredMatchIds != null && !_filteredMatchIds.Contains(match.MatchId))
            return "hidden";

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

            // Update deck profiles for the involved players
                var match = Matches.FirstOrDefault(m => m.MatchId == matchId);
               
               // only update deck profile is the table is the live feed
                if (match != null && tableNumber == LiveFeedTableNumber)
                {
                    await _obsService.UpdateDeckProfilesCommand(match.Player1Name, match.Player2Name);
                }

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
            UpdateSearch(_searchTerm);
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
            UpdateSearch(_searchTerm);
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
        var dialog = await _dialogService.ShowAsync<Pages.Popups.TableSettings>("Table Settings");
        var result = await dialog.Result;
        if (!result.Canceled) await LoadSettingsAsync();
        NotifyChanged();
    }

    private void HandleUpdate(object? s, EventArgs e)
    {
        UpdateMatchesIfChanged(_matchState.GetMatches());
    }

    private void TableAssignmentChanged(object? s, EventArgs e)
    {
        UpdateMatchesIfChanged(_matchState.GetMatches());
    }

    private void UpdateMatchesIfChanged(IReadOnlyList<TournamentMatch> newMatches)
    {
        if (!AreMatchesEqual(Matches, newMatches))
        {
            Matches = newMatches.ToList();
            UpdateSearch(_searchTerm);
            NotifyChanged();
        }
    }

    private static bool AreMatchesEqual(IReadOnlyList<TournamentMatch> current, IReadOnlyList<TournamentMatch> incoming)
    {
        if (current == null && incoming == null)
            return true;
        if (current == null || incoming == null)
            return false;
        if (current.Count != incoming.Count)
            return false;

        var orderedCurrent = current.OrderBy(x => x.MatchId).ToList();
        var orderedIncoming = incoming.OrderBy(x => x.MatchId).ToList();

        for (int i = 0; i < orderedCurrent.Count; i++)
        {
            if (!AreMatchDetailsEqual(orderedCurrent[i], orderedIncoming[i]))
                return false;
        }

        return true;
    }

    private static bool AreMatchDetailsEqual(TournamentMatch a, TournamentMatch b)
    {
        return a.MatchId == b.MatchId
            && a.TournamentId == b.TournamentId
            && a.Round == b.Round
            && a.SuggestedPlayOrder == b.SuggestedPlayOrder
            && a.Player1Id == b.Player1Id
            && a.Player1Name == b.Player1Name
            && a.Player2Id == b.Player2Id
            && a.Player2Name == b.Player2Name
            && a.State == b.State
            && a.WinnerId == b.WinnerId
            && a.LoserId == b.LoserId
            && a.ScoresCsv == b.ScoresCsv
            && a.TableNumber == b.TableNumber
            && a.LastUpdated == b.LastUpdated
            && a.TableAssignedAt == b.TableAssignedAt
            && a.Identifier == b.Identifier
            && a.GroupId == b.GroupId
            && a.Stage == b.Stage;
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