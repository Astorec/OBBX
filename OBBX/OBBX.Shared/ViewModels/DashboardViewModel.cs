using System.Text.Json;
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
    private Dictionary<int, int> RoundMatchTracker = new Dictionary<int, int>();

    public bool IsLoadingTables { get; private set; } = true;
    public bool ObsConnected { get; private set; }
    public bool ObsLive { get; private set; }
    public bool ObsReconnecting { get; private set; }
    public string ChallongeUri { get; private set; } = "";
    public int CurrentRound { get; private set; }
    public int CurrentLoserBracket { get; private set; }
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

        if (settings != null)
        {
            CurrentRound = settings.Challonge.CurrentRound;
            CurrentLoserBracket = settings.Challonge.CurrentLoserBracket;
            CurrentStage = settings.Challonge.CurrentStage;
        }
        
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

    /// <summary>
    /// Get the rounds of the current tournament and update accordingly
    /// </summary>
    /// <returns></returns>
    private async Task GetRounds()
    {

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
        await _matchState.GetCurrentRound();
        var settings = await _appSettings.GetSettingsAsync();

        if (settings.Challonge.CurrentRound != _lastProcessedRound)
        {
            RoundMatchTracker.Clear();
            CurrentRound = settings.Challonge.CurrentRound;
            CurrentLoserBracket = settings.Challonge.CurrentLoserBracket;
            CurrentStage = settings.Challonge.CurrentStage.ToString().Replace("Group", "Group ");
            _lastProcessedRound = settings.Challonge.CurrentRound;
            await UpdateObsOverlayAsync(settings.Challonge.CurrentRound, settings.Challonge.CurrentStage);

            // Only do this if we are in the group stage, we have a finals bracket to display proper 
            if (CurrentStage == "Group Stage")
                await _obsService.StartBracketRotation(RoundMatchTracker);
        }

        NotifyChanged();
    }

    private async Task UpdateObsOverlayAsync(int round, string stage)
    {
        string baseUri = _nav.BaseUri.TrimEnd('/');

        string type = stage.ToLower().Contains("group") ? "swiss" : "bracket";

        switch (stage.ToLower())
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