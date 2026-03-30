using Challonge.Api;
using Challonge.Objects;
using OBBX.Shared.Models;
using System.Text.Json;
using OBBX.Shared.Misc;
using RestSharp;

namespace OBBX.Shared.Services;


public class ChallongeService
{
    #region Variables and Constructor
    private readonly SettingsService _settingsService;
    private ChallongeClient? _client;
    private ChallongeCredentials? _credentials;
    private HttpClient? _httpClient;
    private List<Participant> _participants = new List<Participant>();
    private Dictionary<string, string> _tournamentIdCache = new();


    public ChallongeService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }
    #endregion

    #region Methods

    public bool IsConfigured { get; private set; }
    public string? ConfigurationError { get; private set; }

    public async Task InitChallongeClientAsync()
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            if (string.IsNullOrWhiteSpace(settings.Challonge.ApiKey) ||
              string.IsNullOrWhiteSpace(settings.Challonge.Username))
            {
                IsConfigured = false;
                ConfigurationError = "Challonge API Key or Username is missing.";
                return;
            }
            _httpClient = new HttpClient();
            _credentials = new ChallongeCredentials(settings.Challonge.Username, settings.Challonge.ApiKey);
            _client = new ChallongeClient(_httpClient, _credentials);
            IsConfigured = true;
            ConfigurationError = null;
        }
        catch (Exception ex)
        {
            IsConfigured = false;
            ConfigurationError = $"Initialization failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Fetches the list of participants for a given tournament URL using the Challonge API. If the participants have already been fetched and cached, it returns the cached list unless forceRefresh is set to true, in which case it will fetch a fresh list from the API.
    /// </summary>
    /// <param name="tournamentUrl">URL from Challonge</param>
    /// <param name="forceRefresh">Optional boolean to force a refresh of the cached participants list</param>
    /// <returns></returns>
    public async Task<List<Participant>> GetParticipantsAsync(string tournamentUrl, bool forceRefresh = false)
    {
        var settings = await _settingsService.GetSettingsAsync();

        if (_client == null)
        {
            await InitChallongeClientAsync();
        }

        if (_participants.Count > 0 && !forceRefresh)
        {
            return _participants;
        }

        var tournamentIdentifier = Misc.ChallongeURLConverter.GetTournamentIdentifier(tournamentUrl);
        if (tournamentIdentifier == null)
        {
            return new List<Participant>();
        }

        var participants = await _client!.GetParticipantsAsync(tournamentIdentifier);
        _participants = participants.ToList();
        settings.Challonge.playerCount = _participants.Count();
        return _participants;
    }

    /// <summary>
    /// Clears the cached list of participants, forcing a refresh on the next call to GetParticipantsAsync. This can be useful if you know the participant list has changed and want to ensure you have the latest data from the Challonge API.
    /// </summary>
    public void ClearParticipantsCache()
    {
        _participants.Clear();
    }

    /// <summary>
    /// Fetches the list of stations for a given tournament URL using the Challonge API.
    /// </summary>
    /// <param name="tournamentUrl">URL from Challonge</param>
    /// <returns></returns>
    public async Task<List<Station>> GetStationsAsync(string tournamentUrl)
    {
        if (_credentials == null)
        {
            await InitChallongeClientAsync();
        }

        var tournamentId = ChallongeURLConverter.GetTournamentIdentifier(tournamentUrl);
        if (tournamentId == null)
        {
            return new List<Station>();
        }

        // Having to make a Direct API Call here as Challonge.NET doesn't support V2.1 of the API
        try
        {
            // Originally was going to use HTTP Client for this, but it wasn't giving the expected response from the API.
            // Looking at the Docs, Challonge was using RestSharp for their example for C# so I swapped out to that
            // and we get the calls from there
            var apiKey = _credentials!.ApiKey;
            var url = $"https://api.challonge.com/v2.1/tournaments/{tournamentId}/stations.json";

            var client = new RestClient(url);

            var request = new RestRequest("", Method.Get);

            request.AddHeader("Content-Type", "application/vnd.api+json");
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Authorization-Type", "v1");
            request.AddHeader("Authorization", apiKey);
            RestResponse response = client.Execute(request);

            // Check for successful response
            if (!response.IsSuccessful)
            {
                if(response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    System.Diagnostics.Debug.WriteLine($"Stations endpoint not found. This may be due to an outdated API version. Status Code: {response.StatusCode}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Error fetching stations: {response.StatusCode} - {response.Content}");
                }
                System.Diagnostics.Debug.WriteLine($"Error fetching stations: {response.StatusCode} - {response.Content}");
                return new List<Station>();
            }

            var content = response.Content ?? "";
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var stationsResponse = JsonSerializer.Deserialize<StationsResponse>(content, options);

            // Return the list of stations from the response, or an empty list if the response is null or doesn't contain data
            return stationsResponse?.Data ?? new List<Station>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error fetching stations: {ex.Message}");
            return new List<Station>();
        }
    }

    /// <summary>
    /// Fetch a list of matches from the given Tournament URL provided, and return them as a List of Match Objects
    /// </summary>
    /// <param name="tournamentUrl">URL From Challonge</param>
    /// <returns></returns>
    public async Task<List<Match>> GetMatchesAsync(string tournamentUrl)
    {
        if (string.IsNullOrWhiteSpace(tournamentUrl)) return new List<Match>();
        if (_client == null) await InitChallongeClientAsync();
        if (!IsConfigured) return new List<Match>();

        try
        {
            // Get the tournament identifier from the URL using the ChallongeURLConverter
            var tournamentIdentifier = ChallongeURLConverter.GetTournamentIdentifier(tournamentUrl);
            if (string.IsNullOrEmpty(tournamentIdentifier))
            {
                return new List<Match>();
            }

            var matches = await _client!.GetMatchesAsync(tournamentIdentifier);
            return matches.ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error fetching matches: {ex.Message}");
            return new List<Match>();
        }
    }

    /// <summary>
    /// Dispose of the Challonge Service.
    /// </summary>
    public void Dispose()
    {
        if (_httpClient != null)
        {
            _httpClient.Dispose();
            _httpClient = null;
        }

        _client = null;
        _credentials = null;
        _participants.Clear();
        _tournamentIdCache.Clear();
    }
    #endregion
}