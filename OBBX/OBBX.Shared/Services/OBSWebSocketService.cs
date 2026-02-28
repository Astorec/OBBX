using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OBBX.Shared.Models;
using ObsWebSocket.Core;
using ObsWebSocket.Core.Events.Generated;
using ObsWebSocket.Core.Protocol.Common;
using ObsWebSocket.Core.Protocol.Requests;
using ObsWebSocket.Core.Protocol.Responses;

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