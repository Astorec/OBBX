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
    private readonly ObsWebSocketClient _obsClient;
    private bool _isConnected = false;
    private bool _isLive = false;
    private bool _isReconencting = false;
    private bool _isInitialized = false;
    private bool _isAttemptingConnection = false;
    private string _currentScene = string.Empty;
    private List<SceneStub> _scenes = new List<SceneStub>();
    private List<(int Round, int Page)> _rotationQueue = new();
    private int _currentIndex = 0;

    public OBSWebSocketService(ILogger<OBSWebSocketService> logger, ObsWebSocketClient obsClient)
    {
        _logger = logger;
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
        while (!stoppingToken.IsCancellationRequested && !_isConnected)
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
                _logger.LogWarning("OBS connection failed. Retrying in 5 seconds... Error: {Message}", ex.Message);

                // Wait before trying again
                try
                {
                    await Task.Delay(5000, stoppingToken);
                }
                catch (TaskCanceledException) { break; }
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
        if (!_isConnected)
        {
            _logger.LogWarning("Cannot check live status; not connected to OBS.");
            return;
        }

        try
        {
            try
            {
                var streamingStatus = _obsClient.GetStreamStatusAsync().GetAwaiter().GetResult();
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

            // Update Player 1 Name
            var inputSettingsRequest = new SetInputSettingsRequestData
            {
                InputName = $"Player 1",
                InputSettings = JsonDocument.Parse($"{{ \"text\": \"{table.Player1Name ?? "N/A"}\" }}").RootElement
            };

            await _obsClient.SetInputSettingsAsync(inputSettingsRequest);

            // Update Player 2 Name
            var inputSettingsRequest2 = new SetInputSettingsRequestData
            {
                InputName = $"Player 2",
                InputSettings = JsonDocument.Parse($"{{ \"text\": \"{table.Player2Name ?? "N/A"}\" }}").RootElement
            };

            await _obsClient.SetInputSettingsAsync(inputSettingsRequest2);

            _logger.LogInformation("Updated player names for Table {TableNumber}", table.TableNumber);

            await SwitchScenes($"Table {table.TableNumber}");
        }
        catch (ObsWebSocketException ex)
        {
            _logger.LogError(ex, "Error updating player names: {ErrorMessage}", ex.Message);

            await DisconnectAsync();
            await ConnectWithRetryAsync(CancellationToken.None);
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

    private void OnCurrentProgramSceneChanged(object? sender, CurrentProgramSceneChangedEventArgs e)
    {
        _logger.LogInformation("Event Handler: Program scene changed to {SceneName}", e.EventData.SceneName);
        if (!string.IsNullOrEmpty(e.EventData.SceneName))
        {
            _currentScene = e.EventData.SceneName;
        }
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

    public async Task UpdateBracketVisibility(int round, int page)
    {
        var baseUrl = "http://localhost:5181/overlay/bracket/swiss/";
        var sceneName = "Group Stage";

        //
        var response = await _obsClient.GetSceneItemListAsync(new GetSceneItemListRequestData(sceneName));
        if (response?.SceneItems == null) return;

        bool showingWinners = round > 0;

        foreach (var item in response.SceneItems)
        {
            // Safety check for null names
            if (string.IsNullOrEmpty(item.SourceName)) continue;

            bool isWinnerSource = item.SourceName.Contains("Winners", StringComparison.OrdinalIgnoreCase);
            bool isLoserSource = item.SourceName.Contains("Losers", StringComparison.OrdinalIgnoreCase);

            if (isWinnerSource || isLoserSource)
            {
                bool shouldBeEnabled = isWinnerSource ? showingWinners : !showingWinners;

                // Set the visibility
                await _obsClient.SetSceneItemEnabledAsync(new SetSceneItemEnabledRequestData(
                    sceneName: sceneName,
                    sceneItemId: (double)item.SceneItemId,
                    sceneItemEnabled: shouldBeEnabled
                ));

                if (shouldBeEnabled)
                {
                    var settings = new { url = $"{baseUrl}{round}/{page}" };

                    // Update URL
                    await _obsClient.SetInputSettingsAsync(new SetInputSettingsRequestData(
                        inputName: item.SourceName,
                        inputSettings: JsonSerializer.SerializeToElement(settings)
                    ));
                }
            }
        }
    }

    public async Task StartBracketRotation(Dictionary<int, int> roundData)
    {
        while (true)
        {
            var sortedRounds = roundData.Keys
                .OrderByDescending(k => k > 0)
                .ThenBy(k => k > 0 ? k : Math.Abs(k));

            foreach (var roundKey in sortedRounds)
            {
                int matchCount = roundData[roundKey];
                int pages = (int)Math.Ceiling((double)matchCount / 32);

                for (int p = 1; p <= pages; p++)
                {
                    // This updates visibility and the URL
                    await UpdateBracketVisibility(roundKey, p);

                    await Task.Delay(15000);
                }
            }

        }

    }

    #endregion

    #region Public Methods

    public bool GetLiveStatus
    {
        get
        {
            ObsLiveStatus();
            return _isLive;
        }
    }
    public bool GetReconnectingStatus => _isReconencting;
    public bool GetConnectionStatus => _isConnected;
    public string GetCurrentScene => _currentScene;
    public List<SceneStub> GetScenes => _scenes;

    #endregion
}