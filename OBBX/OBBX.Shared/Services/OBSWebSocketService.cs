using System.Text.Json;
using Microsoft.Extensions.Logging;
using MudBlazor;
using OBBX.Shared.Models;
using ObsWebSocket.Core;
using ObsWebSocket.Core.Events.Generated;
using ObsWebSocket.Core.Protocol.Common;
using ObsWebSocket.Core.Protocol.Requests;

namespace OBBX.Shared.Services;

public class OBSWebSocketService
{
    private readonly ILogger<OBSWebSocketService> _logger;
    private readonly CSVService _csvService;
    private readonly ObsWebSocketClient _obsClient;
    private bool _isConnected = false;
    private bool _isLive = false;
    private bool _isReconencting = false;
    private bool _isInitialized = false;
    private bool _isAttemptingConnection = false;
    private string _currentScene = string.Empty;
    private List<SceneStub> _scenes = new List<SceneStub>();
    private List<(int Round, int Page)> _rotationQueue = new();
    private Dictionary<string, DeckProfile> _onStreamProfiles = new Dictionary<string, DeckProfile>();
    private readonly Dictionary<string, double> _bracketItemIdCache = new();
    private int _maxRetries = 5;
    public OBSWebSocketService(ILogger<OBSWebSocketService> logger, CSVService cSVService, ObsWebSocketClient obsClient)
    {
        _logger = logger;
        _csvService = cSVService;
        _obsClient = obsClient;
        _obsClient.Connected += OnObsConnected;
        _obsClient.Disconnected += OnObsDisconnected;
        _obsClient.CurrentProgramSceneChanged += OnCurrentProgramSceneChanged;
    }

    #region Connection Methods

    public async Task InitAsync(CancellationToken stoppingToken = default)
    {
        if (_isAttemptingConnection) return;

        if (_isInitialized)
        {
            await _csvService.InitializeAsync();
            var scenes = await _obsClient.GetSceneListAsync();
            _scenes = scenes?.Scenes?.ToList() ?? new List<SceneStub>();
            _currentScene = _scenes.Count > 0 ? _scenes[0].SceneName ?? string.Empty : string.Empty;
            return;
        }

        _isAttemptingConnection = true;

        _ = ConnectWithRetryAsync(stoppingToken);
    }

    private async Task ConnectWithRetryAsync(CancellationToken stoppingToken)
    {
        int retries = 0;
        while (!stoppingToken.IsCancellationRequested && !_isConnected && retries < _maxRetries)
        {
            try
            {
                _logger.LogInformation("Attempting to connect to OBS...");
                await _obsClient.ConnectAsync(stoppingToken);

                // If we reach here without an exception, we are connected
                _isInitialized = true;
                _isAttemptingConnection = false;

                // Perform post-connection data fetch
                var scenes = await _obsClient.GetSceneListAsync();
                _scenes = scenes?.Scenes?.ToList() ?? new List<SceneStub>();

                _logger.LogInformation("OBS Connection Established.");
                break; // Exit the retry loop
            }
            catch (Exception ex)
            {
                retries++;

                if (retries >= _maxRetries)
                {
                    _logger.LogWarning("Max retries reached. Waiting before retrying again...");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    retries = 0;
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }
        _isAttemptingConnection = false;
    }

    public async Task DisconnectAsync()
    {
        if (_obsClient.IsConnected)
        {
            _logger.LogInformation("Disconnecting from OBS...");
            await _obsClient.DisconnectAsync();
        }

        _isInitialized = false;
    }

    private void OnObsConnected(object? sender, EventArgs e)
    {
        _logger.LogInformation("Event Handler: Connected to OBS!");
        _isConnected = true;
    }

    private void OnObsDisconnected(object? sender, EventArgs e)
    {
        _logger.LogInformation("Event Handler: Disconnected from OBS!");
        _isConnected = false;
        _isInitialized = false;
    }

    private async Task ObsLiveStatus()
    {
        if (_isAttemptingConnection) return;

        if (!_isConnected)
        {
            _logger.LogWarning("Cannot check live status; not connected to OBS.");
            return;
        }

        try
        {
            try
            {
                var streamingStatus = await _obsClient.GetStreamStatusAsync();
                if (streamingStatus == null)
                {
                    _logger.LogWarning("Failed to retrieve streaming status from OBS.");
                    return;
                }
                else if (streamingStatus.OutputReconnecting)
                {
                    _isReconencting = true;
                    _isLive = false;
                    _logger.LogInformation("OBS is currently reconnecting...");
                }
                else
                {
                    _isReconencting = false;
                    _isLive = streamingStatus.OutputActive;
                    _logger.LogInformation("OBS Live Status: {IsLive}", _isLive);
                }
            }
            catch (ObsWebSocketException ex)
            {
                _logger.LogError(ex, "Error retrieving streaming status: {ErrorMessage}", ex.Message);
                await DisconnectAsync();
                await ConnectWithRetryAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving streaming status: {ErrorMessage}", ex.Message);
            }


        }
        catch (ObsWebSocketException ex)
        {
            _logger.LogError(ex, "Error checking live status: {ErrorMessage}", ex.Message);
        }
    }
    #endregion

    #region Scene Methods

    public async Task UpdatePlayerNameAndPush(TableAssignment table)
    {
        if (!_isConnected)
        {
            _logger.LogWarning("Cannot update player names; not connected to OBS.");
            return;
        }

        try
        {
            await UpdateObsText("Player 1", table.Player1Name ?? "N/A");
            await UpdateObsText("Player 2", table.Player2Name ?? "N/A");

            _logger.LogInformation("Updated player names for Table {TableNumber}", table.TableNumber);

            if (!string.IsNullOrWhiteSpace(table.Player1Name) || !string.IsNullOrWhiteSpace(table.Player2Name))
            {
                await UpdateDeckProfiles(table.Player1Name, table.Player2Name);
            }
            
            await SwitchScenes($"Table {table.TableNumber}");
        }
        catch (ObsWebSocketException ex)
        {
            _logger.LogError(ex, "Error updating player names: {ErrorMessage}", ex.Message);
            await ReconnectAsync();
        }
    }

    public async Task SwitchScenes(string sceneName)
    {

        if (!_isConnected)
        {
            _logger.LogWarning("Cannot switch scenes; not connected to OBS.");
            return;
        }

        try
        {
            await _obsClient.SwitchSceneAndWaitAsync(sceneName);
            _logger.LogInformation("Switched to scene: {SceneName}", sceneName);
        }
        catch (ObsWebSocketException ex)
        {
            _logger.LogError(ex, "Error switching scenes: {ErrorMessage}", ex.Message);
            await DisconnectAsync();
            await ConnectWithRetryAsync(CancellationToken.None);
        }
    }

    private async Task UpdateDeckProfiles(string player1, string player2)
    {
        // Get the new profiles required
        var newProfiles = _csvService.GetCachedProfiles()
            .Where(p => p.Key.Equals(player1, StringComparison.OrdinalIgnoreCase) ||
                        p.Key.Equals(player2, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);


        if (_onStreamProfiles.SequenceEqual(newProfiles)) return;

        // Update the new profiles if we need to
        _onStreamProfiles = newProfiles;

        // Loop through
        foreach (var p in _onStreamProfiles)
        {
            // Determine the OBS prefix (Player 01 or Player 02) based on the name match
            string obsPrefix = p.Key.Equals(player1, StringComparison.OrdinalIgnoreCase) ? "01" : "02";
            var profile = p.Value;

            string mainDeck = BuildDeckString(profile?.Bey01, profile?.Bey02, profile?.Bey03);
            string sideDeck = BuildDeckString(profile?.Bey04, profile?.Bey05);

            await UpdateObsText($"Player {obsPrefix} Beys", mainDeck);
            await UpdateObsText($"Player {obsPrefix} Side Beys", sideDeck);
        }
    }

    public async Task UpdateDeckProfilesCommand(string player1, string player2)
    {
        if (!_isConnected)
        {
            _logger.LogWarning("Cannot update deck profiles; not connected to OBS.");
            return;
        }

        try
        {
            await UpdateDeckProfiles(player1, player2);
        }
        catch (ObsWebSocketException ex)
        {
            _logger.LogError(ex, "Error updating deck profiles: {ErrorMessage}", ex.Message);
            await ReconnectAsync();
        }
    }
    private string BuildDeckString(params string?[] beys)
    {
        return string.Join("\n", beys
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Select(b => $"- {b}"));
    }

    private async Task UpdateObsText(string inputName, string text)
    {
        if (!_isConnected) return;
        try
        {
            var jsonString = JsonSerializer.Serialize(new { text });
            await _obsClient.SetInputSettingsAsync(new SetInputSettingsRequestData
            {
                InputName = inputName,
                InputSettings = JsonDocument.Parse(jsonString).RootElement
            });
        }
        catch (Exception ex) { _logger.LogError(ex, "Text update failed"); }
    }

    private void OnCurrentProgramSceneChanged(object? sender, CurrentProgramSceneChangedEventArgs e)
    {
        _logger.LogInformation("Event Handler: Program scene changed to {SceneName}", e.EventData.SceneName);
        if (!string.IsNullOrEmpty(e.EventData.SceneName))
        {
            _currentScene = e.EventData.SceneName;
        }
    }

    private async Task ReconnectAsync()
    {
        await DisconnectAsync();
        await ConnectWithRetryAsync(CancellationToken.None);
    }
    #endregion

    #region Source Methods
    public async Task UpdateBrowserSourceUrl(string sourceName, string newUrl)
    {
        if (!_isConnected) return;

        try
        {
            var inputSettingsRequest = new SetInputSettingsRequestData
            {
                InputName = sourceName,
                InputSettings = JsonDocument.Parse($"{{ \"url\": \"{newUrl}\" }}").RootElement
            };

            await _obsClient.SetInputSettingsAsync(inputSettingsRequest);
            _logger.LogInformation("Updated OBS Source {Source} to {Url}", sourceName, newUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update browser source URL");
        }
    }

    public async Task PopulateGroupStageScene(Dictionary<int, int> RoundAndMatchCount)
    {
        if (!_isConnected) return;
        string sceneName = "Group Stage";
        try
        {
            var request = new GetSceneItemListRequestData(sceneName);
            var response = await _obsClient.GetSceneItemListAsync(request);

            if (response?.SceneItems != null)
            {
                foreach (var item in response.SceneItems)
                {
                    var removeRequest = new RemoveSceneItemRequestData((double)item.SceneItemId, sceneName);
                    await _obsClient.RemoveSceneItemAsync(removeRequest);
                }
            }

            var baseUrl = "http://localhost:5181/overlay/bracket/swiss/";

            foreach (var value in RoundAndMatchCount)
            {
                var sourceName = "";
                var pages = (int)Math.Ceiling((double)value.Value / 32);


                for (int i = 1; i <= pages; i++)
                {
                    bool show = false;
                    if (value.Key > 0)
                    {
                        sourceName = $"Winners Bracket Round {value.Key:D2} Page {i:D2}";
                        if (i > 1)
                            show = true;
                    }
                    else
                        sourceName = $"Losers Bracket Round {MathF.Abs(value.Key)} Page {i:D2}";
                    var settingsObject = new
                    {
                        url = $"{baseUrl}{value.Key}/{i}",
                        width = 1920,
                        height = 1080
                    };
                    var settingsJson = JsonSerializer.SerializeToElement(settingsObject);
                    var createRequest = new CreateInputRequestData(sourceName, "browser_source", sceneName, inputSettings: settingsJson);
                    await _obsClient.CreateInputAsync(createRequest);
                }
            }



        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to populate Group Stage Scene");
        }
    }
    #endregion

    #region Advanced Scene Switcher Methods
    public async Task StartBracketRotation(Dictionary<int, int> roundData, CancellationToken ct)
    {
        const string sceneName = "Group Stage";
        const string baseUrl = "http://localhost:5181/overlay/bracket/swiss/";

        try
        {
            await InitializeObsSourcesAsync(sceneName, roundData, baseUrl);

            await SyncBracketSources(sceneName, roundData);

            while (!ct.IsCancellationRequested)
            {
                var sortedRounds = roundData.Keys
                    .OrderByDescending(k => k > 0)
                    .ThenBy(k => k > 0 ? k : Math.Abs(k));

                foreach (var roundKey in sortedRounds)
                {
                    int matchCount = roundData[roundKey];
                    // Ensure we ALWAYS round up. 33 matches = 2 pages.
                    int totalPages = (int)Math.Ceiling((double)matchCount / 32);

                    Console.WriteLine($"[Rotation] Round {roundKey} has {matchCount} matches. Total Pages: {totalPages}");

                    for (int p = 1; p <= totalPages; p++)
                    {
                        if (ct.IsCancellationRequested) return;

                        Console.WriteLine($"[Rotation] Showing {roundKey} Page {p} of {totalPages}");
                        var prefix = roundKey > 0 ? "Winners" : "Losers";
                        await ShowBracketPage(sceneName, prefix, roundKey, p, baseUrl);

                        await Task.Delay(15000, ct);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ShowBracketPage(string sceneName, string prefix, int round, int page, string baseUrl)
    {
        string targetName = $"{prefix}_P{page}";
        string targetUrl = $"{baseUrl}{round}/{page}";

        try
        {
            var getSettingsRequest = new GetInputSettingsRequestData(targetName);
            var currentSettingsResponse = await _obsClient.GetInputSettingsAsync(getSettingsRequest);
            if (currentSettingsResponse?.InputSettings is JsonElement settingsElement)
            {
                if (settingsElement.TryGetProperty("url", out var urlElement))
                {
                    if (urlElement.GetString() != targetUrl)
                    {
                        var settings = new { url = targetUrl };
                        await _obsClient.SetInputSettingsAsync(new SetInputSettingsRequestData(
                            inputName: targetName,
                            inputSettings: JsonSerializer.SerializeToElement(settings)
                        ));

                        _logger.LogInformation("URL Updated for {Target}: {Url}", targetName, targetUrl);
                    }
                }
            }
        }
        catch
        {
            
        }
        
        foreach (var kvp in _bracketItemIdCache)
        {
            bool shouldBeVisible = (kvp.Key == targetName);

            // Only send the command if we are certain it's a bracket source
            if (kvp.Key.StartsWith("Winners_") || kvp.Key.StartsWith("Losers_"))
            {
                await _obsClient.SetSceneItemEnabledAsync(new SetSceneItemEnabledRequestData(
                    sceneName: sceneName,
                    sceneItemId: kvp.Value,
                    sceneItemEnabled: shouldBeVisible
                ));
            }
        }
    }

    private async Task SyncBracketSources(string sceneName, Dictionary<int, int> roundData)
    {
        var response = await _obsClient.GetSceneItemListAsync(new GetSceneItemListRequestData(sceneName));
        var currentObsSources = response.SceneItems
            .Where(i => i.SourceName.Contains("Winners_P") || i.SourceName.Contains("Losers_P"))
            .ToList();

        var validSourceNames = new HashSet<string>();
        foreach (var round in roundData)
        {
            string prefix = round.Key > 0 ? "Winners" : "Losers";
            int totalPages = (int)Math.Ceiling((double)round.Value / 32);
            for (int p = 1; p <= totalPages; p++)
            {
                validSourceNames.Add($"{prefix}_P{p}");
            }
        }

        foreach (var item in currentObsSources)
        {
            if (!validSourceNames.Contains(item.SourceName))
            {
                await _obsClient.RemoveInputAsync(new RemoveInputRequestData(item.SourceName));
            }
        }

    }

    public async Task InitializeObsSourcesAsync(string sceneName, Dictionary<int, int> roundData, string baseUrl)
    {
        var response = await _obsClient.GetSceneItemListAsync(new GetSceneItemListRequestData(sceneName));
        _bracketItemIdCache.Clear();

        // Cache existing IDs immediately
        if (response?.SceneItems != null)
        {
            foreach (var item in response.SceneItems)
                _bracketItemIdCache[item.SourceName] = (double)item.SceneItemId;
        }

        foreach (var round in roundData)
        {
            string prefix = round.Key > 0 ? "Winners" : "Losers";
            int totalPages = (int)Math.Ceiling((double)round.Value / 32);

            for (int p = 1; p <= totalPages; p++)
            {
                string targetName = $"{prefix}_P{p}";
                if (!_bracketItemIdCache.ContainsKey(targetName))
                {
                    var settings = new { url = $"{baseUrl}{round.Key}/{p}", width = 1920, height = 1080 };

                    // Create the input
                    await _obsClient.CreateInputAsync(new CreateInputRequestData(
                        sceneName: sceneName,
                        inputName: targetName,
                        inputKind: "browser_source",
                        inputSettings: JsonSerializer.SerializeToElement(settings),
                        sceneItemEnabled: false
                    ));

                    // Re-fetch list once to get the new ID or predict it if possible
                    // Better: just refresh the whole cache once after the loop if items were added
                }
            }
        }

        // Refresh cache one last time to ensure we have IDs for newly created items
        var finalItems = await _obsClient.GetSceneItemListAsync(new GetSceneItemListRequestData(sceneName));
        foreach (var item in finalItems.SceneItems)
            _bracketItemIdCache[item.SourceName] = (double)item.SceneItemId;
    }

    #endregion

    #region Public Methods

    public async Task<bool> GetLiveStatus()
    {
        await ObsLiveStatus();
        return _isLive;
    }
    public async Task<bool> GetReconnectingStatus()
    {
        return _isReconencting;
    }
    public async Task<bool> GetConnectionStatus()
    {
        return _isConnected;
    }
    public string GetCurrentScene => _currentScene;
    public List<SceneStub> GetScenes => _scenes;

    #endregion
}