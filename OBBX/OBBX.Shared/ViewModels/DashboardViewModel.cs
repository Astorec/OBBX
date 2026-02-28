using Microsoft.AspNetCore.Components;
using MudBlazor;
using OBBX.Shared.Models;
using OBBX.Shared.Services;
using ObsWebSocket.Core.Protocol.Common;

namespace OBBX.Shared.ViewModels;

public class DashboardViewModel : IDisposable
{
    private readonly SettingsService _appSettings;
    private readonly OBSWebSocketService _obsService;
    private readonly MatchStateService _matchState;
    private readonly ISnackbar _snackbar;
    private readonly NavigationManager _nav;
    private CancellationTokenSource? _cts;
    private int _lastProcessedRound = -1;

    public bool IsLoadingTables { get; private set; } = true;
    public bool ObsConnected { get; private set; }
    public bool ObsLive { get; private set; }
    public bool ObsReconnecting { get; private set; }
    public string ChallongeUri { get; private set; } = "";
    public int CurrentRound { get; private set; }
    public string CurrentStage { get; private set; } = "";
    public string SelectedScene { get; set; } = "";
    public string SceneToPush { get; set; } = "";
    public List<TournamentMatch> TableMatches { get; private set; } = new();
    public List<SceneStub> Scenes { get; private set; } = new();
    public int LiveFeedTableNumber { get; private set; } = 1;

    public Color ObsStatusColor => ObsConnected ? Color.Success : Color.Error;
    public Color ObsLiveColor => ObsLive ? Color.Success : (ObsReconnecting ? Color.Warning : Color.Error);

    // Event to notify the Razor component to call StateHasChanged
    public event Action? OnChanged;

    public DashboardViewModel(
        SettingsService appSettings,
        OBSWebSocketService obsService,
        MatchStateService matchState,
        ISnackbar snackbar,
        NavigationManager nav)
    {
        _appSettings = appSettings;
        _obsService = obsService;
        _matchState = matchState;
        _snackbar = snackbar;
        _nav = nav;
    }

    public async Task InitializeAsync()
    {
        var settings = await _appSettings.GetSettingsAsync();
        ChallongeUri = settings.Challonge?.Uri ?? "";
        LiveFeedTableNumber = settings.Tables?.LiveFeedTableNumber ?? 1;

        _matchState.MatchesUpdated += HandleMatchesUpdated;
        _matchState.TableAssignmentChanged += HandleTableAssignmentChanged;

        _cts = new CancellationTokenSource();
        _ = RunConnectionMonitorAsync(_cts.Token);
    }

    public async Task LoadInitialDataAsync()
    {
        try
        {
            await _obsService.InitAsync();
            if (!string.IsNullOrEmpty(ChallongeUri))
            {
                if (!_matchState.IsInitialized)
                    await _matchState.InitializeAsync(ChallongeUri);

                TableMatches = _matchState.GetActiveTableMatches().ToList();
            }
        }
        finally
        {
            IsLoadingTables = false;
            NotifyChanged();
        }
    }

    public async Task PushSceneAsync(string sceneName = "")
    {
        string target = string.IsNullOrEmpty(sceneName) ? SceneToPush : sceneName;

        if (string.IsNullOrEmpty(target))
        {
            _snackbar.Add("Please select a scene.", Severity.Warning);
            return;
        }

        await _obsService.SwitchScenes(target);
        SelectedScene = target;
        NotifyChanged();
    }

    public async Task SelectTableAsync(TournamentMatch match)
    {
        if (match == null) return;

        var assignment = new TableAssignment
        {
            TableNumber = match.TableNumber,
            Player1Name = match.Player1Name ?? "TBD",
            Player2Name = match.Player2Name ?? "TBD",
            MatchId = match.MatchId
        };

        await _obsService.UpdatePlayerNameAndPush(assignment);
        _snackbar.Add($"Table {match.TableNumber} pushed to OBS.", Severity.Info);
    }

    private async Task RunConnectionMonitorAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            ObsConnected = _obsService.GetConnectionStatus;
            ObsLive = _obsService.GetLiveStatus;
            ObsReconnecting = _obsService.GetReconnectingStatus;

            NotifyChanged();
            await Task.Delay(5000, ct);
        }
    }

    private async void HandleMatchesUpdated(object? s, MatchesUpdatedEventArgs e)
    {
        TableMatches = _matchState.GetActiveTableMatches().ToList();
        var current = _matchState.GetCurrentRound();
        var currentRoundData = _matchState.GetCurrentRound(); // Using the method we discussed earlier

        if (currentRoundData.Round != _lastProcessedRound)
        {            
            CurrentRound = current.Round;
            CurrentStage = current.Stage.ToString().Replace("Group", "Group ");
            _lastProcessedRound = current.Round;
            await UpdateObsOverlayAsync(currentRoundData.Round, currentRoundData.Stage);
        }

        NotifyChanged();
    }

    private async Task UpdateObsOverlayAsync(int round, string stage)
    {
        // 1. Get the base address (e.g., http://localhost:5181)
        string baseUri = _nav.BaseUri.TrimEnd('/');

        // 2. Build the new URL based on your logic (assuming swiss/bracket logic)
        string type = stage.ToLower().Contains("group") ? "swiss" : "bracket";

        // 3. Push to OBS (Replace "BracketOverlay" with your actual source name in OBS)

        switch(stage.ToLower())
        {
            case string s when s.Contains("group"):
                await _obsService.UpdateBrowserSourceUrl("Stage", $"{baseUri}/overlay/bracket/{type}/{round}");
                break;
            case string s when s.Contains("finals"):
                await _obsService.UpdateBrowserSourceUrl("Stage", $"{baseUri}/overlay/bracket/single");
                break;
            default:
                await _obsService.UpdateBrowserSourceUrl("Stage", $"{baseUri}/overlay/bracket/{type}/{round}");
                break;
        }
    }

    private void HandleTableAssignmentChanged(object? s, TableAssignmentChangedEventArgs e)
    {
        TableMatches = _matchState.GetActiveTableMatches().ToList();
        NotifyChanged();
    }

    private void NotifyChanged() => OnChanged?.Invoke();

    public void Dispose()
    {
        _cts?.Cancel();
        _matchState.MatchesUpdated -= HandleMatchesUpdated;
        _matchState.TableAssignmentChanged -= HandleTableAssignmentChanged;
    }
}