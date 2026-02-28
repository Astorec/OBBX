using System.Diagnostics;
using System.Text.Json;
using Challonge.Objects;
using OBBX.Shared.Models;

namespace OBBX.Shared.Services;

/// <summary>
/// Manages match state, table assignments, and synchronization with Challonge.
/// Handles periodic updates for dynamically generated matches (Swiss/Elimination).
/// </summary>
public class MatchStateService : IDisposable
{
    #region Variables
    private readonly ChallongeService _challongeService;
    private readonly SettingsService _settingsService;

    private Dictionary<long, TournamentMatch> _matches = new();
    private Dictionary<long, Participant> _participants = new();
    private Dictionary<long, Participant> _groupPlayerIdToParticipant = new(); private Timer? _refreshTimer;
    private bool _isRefreshing;
    private string? _currentTournamentUrl;
    private int _currentRound;
    #endregion

    #region  Event Handlers and Args
    public event EventHandler<MatchesUpdatedEventArgs>? MatchesUpdated;

    public event EventHandler<TableAssignmentChangedEventArgs>? TableAssignmentChanged;

    public MatchStateService(ChallongeService challongeService, SettingsService settingsService)
    {
        _challongeService = challongeService;
        _settingsService = settingsService;
    }
    #endregion

    /// <summary>
    /// Initialize the MatchStateService with a tournament URL. Loads initial data and starts periodic refresh.
    /// </summary>
    /// <param name="tournamentUrl">URL from challonge</param>
    /// <returns></returns>
    public async Task InitializeAsync(string tournamentUrl)
    {
        _currentTournamentUrl = tournamentUrl;

        // Load any persisted table assignments
        await LoadTableAssignmentsAsync();

        // Initial data load
        await RefreshFromChallongeAsync();

        // Start periodic refresh based on settings
        var settings = await _settingsService.GetSettingsAsync();
        var interval = TimeSpan.FromSeconds(settings.Challonge.RefreshIntervalSeconds);
        _refreshTimer = new Timer(async _ => await RefreshFromChallongeAsync(), null, interval, interval);
    }

    /// <summary>
    /// Reset the Service when the URL has been changed
    /// </summary>
    /// <param name="tournamentUrl">The new tournament URL</param>
    /// <returns></returns>
    public async Task ResetForNewTournamentAsync(string tournamentUrl)
    {
        // Stop existing periodic refresh
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        _isRefreshing = false;

        // Clear in-memory caches
        _matches.Clear();
        _participants.Clear();
        _groupPlayerIdToParticipant.Clear();
        _currentTournamentUrl = null;

        // Clear any cached Challonge data in the client service
        _challongeService.ClearParticipantsCache();

        // Clear persisted table assignments so a new tournament starts clean
        var tablePath = GetTableAssignmentsPath();
        if (File.Exists(tablePath))
        {
            try
            {
                File.Delete(tablePath);
            }
            catch
            {
                // Ignore IO errors; worst case old assignments are re-used
            }
        }

        // Reinit the challonge service with the new tournament URL
        _challongeService.Dispose();
        await _challongeService.InitChallongeClientAsync();

        // Initialize with the new tournament URL
        await InitializeAsync(tournamentUrl);
    }

    /// <summary>
    /// Get matches with an optional filter for state, stage, round, table assignment, playability, or search term. Used for various UI displays and logic.
    /// </summary>
    /// <param name="filter">The filter to apply to the matches</param>
    /// <returns></returns>
    public IReadOnlyList<TournamentMatch> GetMatches(MatchFilter? filter = null)
    {
        var matches = _matches.Values.AsEnumerable();

        if (filter != null)
        {
            // Apply filters based on the provided criteria
            if (filter.State.HasValue)
            {
                matches = matches.Where(m => m.State == filter.State.Value);
            }

            if (filter.Stage.HasValue)
            {
                matches = matches.Where(m => m.Stage == filter.Stage.Value);
            }

            if (filter.Round.HasValue)
            {
                matches = matches.Where(m => m.Round == filter.Round.Value);
            }

            if (filter.HasTableAssigned.HasValue)
            {
                matches = matches.Where(m => m.HasTableAssigned == filter.HasTableAssigned.Value);
            }

            if (filter.IsPlayable.HasValue)
            {
                matches = matches.Where(m => m.IsPlayable == filter.IsPlayable.Value);
            }

            if (!string.IsNullOrEmpty(filter.SearchTerm))
            {
                var term = filter.SearchTerm.ToLowerInvariant();
                matches = matches.Where(m =>
                    (m.Player1Name?.ToLowerInvariant().Contains(term) ?? false) ||
                    (m.Player2Name?.ToLowerInvariant().Contains(term) ?? false) ||
                    (m.Identifier?.ToLowerInvariant().Contains(term) ?? false));
            }
        }

        return matches
            .OrderBy(m => m.Round)
            .ThenBy(m => m.SuggestedPlayOrder)
            .ToList();
    }

    /// <summary>
    /// Get matches that have tables assigned and are not complete, ordered by table number. Used for active table display and logic.
    /// </summary>
    /// <returns></returns>
    public IReadOnlyList<TournamentMatch> GetActiveTableMatches()
    {
        return _matches.Values
            .Where(m => m.HasTableAssigned && m.State != TournamentMatchState.Complete)
            .OrderBy(m => m.TableNumber)
            .ToList();
    }

    /// <summary>
    /// Get bracket information (type, stage name, points format) based on the current player count. 
    /// Used for display and logic that depends on the tournament format.
    /// </summary>
    /// <returns></returns>
    public BracketInfo GetBracketInfo()
    {
        return BracketInfo.FromPlayerCount(_participants.Count);
    }

    // Helper method to check if the service has been initialized with a tournament URL and has loaded participants
    public bool IsInitialized => _currentTournamentUrl != null && _participants.Count > 0;

    /// <summary>
    /// Manually refresh table assignments from Challonge stations
    /// Useful when user wants to sync without a full match refresh
    /// </summary>
    public async Task RefreshStationsAsync()
    {
        if (string.IsNullOrEmpty(_currentTournamentUrl))
        {
            return;
        }

        try
        {
            await RefreshStationsAndAutoPopulateTablesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MatchState] Error refreshing stations: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Get matches organized by round for bracket display
    /// </summary>
    public Dictionary<int, List<TournamentMatch>> GetMatchesByRound(MatchStage? stageFilter = null)
    {
        var matches = stageFilter.HasValue
            ? _matches.Values.Where(m => m.Stage == stageFilter.Value)
            : _matches.Values;

        return matches
            .GroupBy(m => m.Round)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.SuggestedPlayOrder ?? 0).ToList());
    }

    /// <summary>
    /// Get winners bracket matches (non-negative rounds) for single/double elimination
    /// Includes round 0 which is the 3rd place match in Challonge API
    /// </summary>
    public Dictionary<int, List<TournamentMatch>> GetWinnersBracketMatches()
    {
        Console.WriteLine($"[GetWinnersBracketMatches] Total _matches: {_matches.Count}");

        var allMatches = _matches.Values.ToList();
        Console.WriteLine($"  All matches in _matches:");
        foreach (var m in allMatches.OrderBy(x => x.Round))
        {
            Console.WriteLine($"    ID={m.MatchId}, Round={m.Round}, Stage={m.Stage}, Players={m.Player1Name} vs {m.Player2Name}");
        }

        var filtered = allMatches
            .Where(m => m.Round >= 0)
            .ToList();

        Console.WriteLine($"  After filtering (Round >= 0 && Stage == Finals): {filtered.Count} matches");
        foreach (var m in filtered.OrderBy(x => x.Round))
        {
            Console.WriteLine($" [Filtered Matches]  ID={m.MatchId}, Round={m.Round}, Stage={m.Stage}, Players={m.Player1Name} vs {m.Player2Name}");
        }

        var result = filtered
            .GroupBy(m => m.Round)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.SuggestedPlayOrder ?? 0).ToList());

        Console.WriteLine($"  Final winners bracket dictionary: {result.Count} rounds");
        foreach (var round in result)
        {
            Console.WriteLine($"    Round {round.Key}: {round.Value.Count} matches");
        }

        return result;
    }

    /// <summary>
    /// Get losers bracket matches (negative rounds) for double elimination
    /// </summary>
    public Dictionary<int, List<TournamentMatch>> GetLosersBracketMatches()
    {
        return _matches.Values
            .Where(m => m.Round < 0)
            .GroupBy(m => Math.Abs(m.Round))
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.SuggestedPlayOrder ?? 0).ToList());
    }

    /// <summary>
    /// Get participant standings for Round Robin or Swiss display
    /// </summary>
    public List<ParticipantStanding> GetParticipantStandings()
    {
        var standings = new Dictionary<long, ParticipantStanding>();

        // Initialize standings for all participants
        foreach (var participant in _participants.Values)
        {
            standings[participant.Id] = new ParticipantStanding
            {
                ParticipantId = participant.Id,
                Name = participant.Name ?? participant.DisplayName ?? "Unknown",
                Seed = participant.Seed
            };
        }

        // Calculate wins/losses from completed matches
        foreach (var match in _matches.Values.Where(m => m.State == TournamentMatchState.Complete))
        {
            if (match.WinnerId.HasValue && standings.ContainsKey(match.WinnerId.Value))
            {
                standings[match.WinnerId.Value].Wins++;
                standings[match.WinnerId.Value].MatchesPlayed++;
            }

            if (match.LoserId.HasValue && standings.ContainsKey(match.LoserId.Value))
            {
                standings[match.LoserId.Value].Losses++;
                standings[match.LoserId.Value].MatchesPlayed++;
            }
        }

        // Sort by wins descending, then losses ascending
        return standings.Values
            .OrderByDescending(s => s.Wins)
            .ThenBy(s => s.Losses)
            .ThenBy(s => s.Seed)
            .ToList();
    }

    /// <summary>
    /// Get matches grouped by GroupId for group stage display
    /// </summary>
    public Dictionary<long, List<TournamentMatch>> GetMatchesByGroup()
    {
        return _matches.Values
            .Where(m => m.GroupId.HasValue)
            .GroupBy(m => m.GroupId!.Value)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Round).ThenBy(m => m.SuggestedPlayOrder ?? 0).ToList());
    }

    /// <summary>
    /// Assign a table to a match
    /// </summary>
    public async Task AssignTableAsync(long matchId, int tableNumber)
    {
        if (!_matches.TryGetValue(matchId, out var match))
        {
            throw new InvalidOperationException($"Match {matchId} not found");
        }

        var previousTable = match.TableNumber;
        match.TableNumber = tableNumber;
        match.TableAssignedAt = tableNumber > 0 ? DateTime.UtcNow : null;

        // Persist the assignment
        await SaveTableAssignmentsAsync();

        TableAssignmentChanged?.Invoke(this, new TableAssignmentChangedEventArgs
        {
            Match = match,
            PreviousTableNumber = previousTable,
            NewTableNumber = tableNumber
        });
    }

    /// <summary>
    /// Clear table assignment for a match
    /// </summary>
    public async Task ClearTableAssignmentAsync(long matchId)
    {
        await AssignTableAsync(matchId, 0);
    }

    /// <summary>
    /// Force a refresh from Challonge
    /// </summary>
    public async Task RefreshFromChallongeAsync()
    {
        if (_isRefreshing || string.IsNullOrEmpty(_currentTournamentUrl))
        {
            return;
        }

        _isRefreshing = true;

        try
        {
            // Get fresh data from Challonge
            var participants = await _challongeService.GetParticipantsAsync(_currentTournamentUrl);
            var challongeMatches = await _challongeService.GetMatchesAsync(_currentTournamentUrl);

            // Update participant cache - keyed by main participant ID
            _participants = participants.ToDictionary(p => p.Id);

            // Build mapping from group_player_ids to participant
            // In Challonge, matches use group_player_ids (not the main participant id) when in groups
            _groupPlayerIdToParticipant = BuildGroupPlayerIdMapping(participants);

            // Track which matches are new or updated
            var addedMatches = new List<TournamentMatch>();
            var updatedMatches = new List<TournamentMatch>();

            foreach (var challongeMatch in challongeMatches)
            {
                var isNew = !_matches.TryGetValue(challongeMatch.Id, out var existingMatch);

                var matchInfo = MapToMatchInfo(challongeMatch, existingMatch);

                if (isNew)
                {
                    _matches[challongeMatch.Id] = matchInfo;
                    addedMatches.Add(matchInfo);
                }
                else if (HasMatchChanged(existingMatch!, matchInfo))
                {
                    // Preserve table assignment when updating
                    matchInfo.TableNumber = existingMatch!.TableNumber;
                    matchInfo.TableAssignedAt = existingMatch.TableAssignedAt;
                    _matches[challongeMatch.Id] = matchInfo;
                    updatedMatches.Add(matchInfo);
                }
            }

            // Remove matches that no longer exist in Challonge
            var currentIds = challongeMatches.Select(m => m.Id).ToHashSet();
            var removedIds = _matches.Keys.Where(id => !currentIds.Contains(id)).ToList();
            foreach (var id in removedIds)
            {
                _matches.Remove(id);
            }

            // Debug logging
            Console.WriteLine($"[MatchState] Total matches loaded: {_matches.Count}");
            foreach (var match in _matches.Values.OrderBy(m => m.Round))
            {
                Console.WriteLine($"  Match {match.MatchId}: Round={match.Round}, Stage={match.Stage}, IsPlayable={match.IsPlayable}, Players={match.Player1Name} vs {match.Player2Name}");
            }

            // Notify listeners
            if (addedMatches.Any() || updatedMatches.Any() || removedIds.Any())
            {
                MatchesUpdated?.Invoke(this, new MatchesUpdatedEventArgs
                {
                    AddedMatches = addedMatches,
                    UpdatedMatches = updatedMatches,
                    RemovedMatchIds = removedIds
                });
            }

            // Auto-populate tables from Challonge stations
            await RefreshStationsAndAutoPopulateTablesAsync();
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    public (int Round, string Stage) GetCurrentRound()
    {
        if (!_matches.Any()) return (0, "Unknown");

        // 1. Get all matches that are playable (ready to be started) 
        // or currently in progress, but not finished.
        var activeMatches = _matches.Values
            .Where(m => m.IsPlayable && m.State != TournamentMatchState.Complete)
            .ToList();

        // 2. If all matches are finished, the tournament might be over.
        if (!activeMatches.Any())
        {
            var lastMatch = _matches.Values.OrderByDescending(m => m.Round).FirstOrDefault();
            return (lastMatch?.Round ?? 0, lastMatch?.Stage.ToString() ?? "Unknown");
        }

        // 3. Determine the "lowest" round currently active. 
        // We group by Stage first because Round 1 of Finals is "later" than Round 3 of Groups.
        var currentMatch = activeMatches
            .OrderBy(m => GetStagePriority(m.Stage.ToString())) // Ensure Groups come before Finals
            .ThenBy(m => m.Round == 0 ? 999 : m.Round) // Treat Round 0 (3rd place) as high/last
            .First();

        return (currentMatch.Round, currentMatch.Stage.ToString());
    }

    private int GetStagePriority(string stage)
    {
        // Ensure Groups (or "play_off") come before "final" or "bracket"
        return stage.ToLower().Contains("group") ? 1 : 2;
    }

    /// <summary>
    /// Map a Challonge Match to our TournamentMatch model
    /// </summary>
    private TournamentMatch MapToMatchInfo(Match challongeMatch, TournamentMatch? existing = null)
    {
        var matchInfo = existing ?? new TournamentMatch();

        matchInfo.MatchId = challongeMatch.Id;
        matchInfo.TournamentId = challongeMatch.TournamentId;
        matchInfo.Round = challongeMatch.Round;
        matchInfo.SuggestedPlayOrder = challongeMatch.SuggestedPlayOrder;
        matchInfo.GroupId = challongeMatch.GroupId;
        matchInfo.Identifier = challongeMatch.Identifier;
        matchInfo.ScoresCsv = FormatScores(challongeMatch.Scores);
        matchInfo.LastUpdated = DateTime.UtcNow;

        // Handle Player 1 (may be null for undetermined bracket positions)
        if (challongeMatch.Player1Id.HasValue)
        {
            matchInfo.Player1Id = challongeMatch.Player1Id;
            matchInfo.Player1Name = GetParticipantName(challongeMatch.Player1Id.Value);
        }
        else
        {
            matchInfo.Player1Id = null;
            matchInfo.Player1Name = null;
        }

        // Handle Player 2 (may be null for undetermined bracket positions)
        if (challongeMatch.Player2Id.HasValue)
        {
            matchInfo.Player2Id = challongeMatch.Player2Id;
            matchInfo.Player2Name = GetParticipantName(challongeMatch.Player2Id.Value);
        }
        else
        {
            matchInfo.Player2Id = null;
            matchInfo.Player2Name = null;
        }

        // Map match state
        matchInfo.State = MapMatchState(challongeMatch.State);
        matchInfo.WinnerId = challongeMatch.WinnerId;
        matchInfo.LoserId = challongeMatch.LoserId;

        // Determine the stage of this match
        matchInfo.Stage = DetermineMatchStage(challongeMatch);
        return matchInfo;
    }

    /// <summary>
    /// Determine if a match is Group Stage or Finals by checking if players are using their main IDs or group_player_ids.
    /// - If either player is using a group_player_id (not their main participant ID), it's Group Stage
    /// - If both players are using their main participant IDs, it's Finals (which includes rounds 0+, where 0 is third-place)
    /// </summary>
    private MatchStage DetermineMatchStage(Match challongeMatch)
    {
        // Check if Player1 is using a group_player_id (not their main participant ID)
        bool player1UsesGroupId = challongeMatch.Player1Id.HasValue &&
                                  _groupPlayerIdToParticipant.ContainsKey(challongeMatch.Player1Id.Value) &&
                                  !_participants.ContainsKey(challongeMatch.Player1Id.Value);

        // Check if Player2 is using a group_player_id (not their main participant ID)
        bool player2UsesGroupId = challongeMatch.Player2Id.HasValue &&
                                  _groupPlayerIdToParticipant.ContainsKey(challongeMatch.Player2Id.Value) &&
                                  !_participants.ContainsKey(challongeMatch.Player2Id.Value);

        // If either player is using a group_player_id, it's GroupStage
        if (player1UsesGroupId || player2UsesGroupId)
        {
            return MatchStage.GroupStage;
        }

        // Otherwise it's Finals (includes rounds 0 to x, where 0 is third-place)
        return MatchStage.Finals;
    }

    private TournamentMatchState MapMatchState(MatchState challongeState)
    {
        return challongeState switch
        {
            MatchState.Pending => TournamentMatchState.Pending,
            MatchState.Open => TournamentMatchState.Open,
            MatchState.Complete => TournamentMatchState.Complete,
            _ => TournamentMatchState.Pending
        };
    }

    private string? FormatScores(IEnumerable<Score>? scores)
    {
        if (scores == null || !scores.Any())
        {
            return null;
        }

        // Convert scores to string format - Score objects typically have ToString() implementations
        return string.Join(",", scores.Select(s => s.ToString()));
    }

    /// <summary>
    /// Build a mapping from group_player_ids to participants.
    /// In Challonge, when using groups/pools, matches reference group_player_ids 
    /// (found in participant.GroupPlayerIds) rather than the main participant.Id
    /// </summary>
    private Dictionary<long, Participant> BuildGroupPlayerIdMapping(IEnumerable<Participant> participants)
    {
        var mapping = new Dictionary<long, Participant>();

        foreach (var participant in participants)
        {
            // Add the main participant ID
            mapping[participant.Id] = participant;

            // Also add any group_player_ids that map to this participant
            if (participant.GroupPlayerIds != null)
            {
                foreach (var groupPlayerId in participant.GroupPlayerIds)
                {
                    mapping[groupPlayerId] = participant;
                }
            }
        }

        return mapping;
    }

    /// <summary>
    /// Get participant name by ID - checks both main participant IDs and group_player_ids
    /// Returns "TBD" if participant cannot be found
    /// </summary>
    private string GetParticipantName(long participantId)
    {
        // First try the group player ID mapping (includes both main IDs and group_player_ids)
        if (_groupPlayerIdToParticipant.TryGetValue(participantId, out var participant))
        {
            return participant.DisplayName ?? participant.Name ?? "TBD";
        }

        // Fallback to main participants dictionary
        if (_participants.TryGetValue(participantId, out var mainParticipant))
        {
            return mainParticipant.DisplayName ?? mainParticipant.Name ?? "TBD";
        }

        // Not found - return TBD
        return "TBD";
    }

    private bool HasMatchChanged(TournamentMatch existing, TournamentMatch updated)
    {
        return existing.Player1Id != updated.Player1Id ||
               existing.Player2Id != updated.Player2Id ||
               existing.State != updated.State ||
               existing.WinnerId != updated.WinnerId ||
               existing.ScoresCsv != updated.ScoresCsv ||
               existing.Round != updated.Round;
    }

    /// <summary>
    /// Fetch stations (tables) from Challonge API and auto-populate table assignments
    /// </summary>
    private async Task RefreshStationsAndAutoPopulateTablesAsync()
    {
        if (string.IsNullOrEmpty(_currentTournamentUrl))
        {
            return;
        }

        try
        {
            var stations = await _challongeService.GetStationsAsync(_currentTournamentUrl);
            Console.WriteLine($"[MatchState] Fetched {stations.Count} stations from Challonge");

            // First, track old assignments
            var previouslyAssignedMatches = _matches.Values.Where(m => m.TableNumber > 0).ToList();

            // Build new assignments map from stations
            var newAssignments = new Dictionary<long, int>(); // matchId -> tableNumber

            foreach (var station in stations)
            {
                try
                {
                    // Extract table number from station name (e.g., "Table 1" -> 1)
                    var tableNumber = ExtractTableNumber(station.Attributes.Name);
                    if (tableNumber <= 0)
                    {
                        Console.WriteLine($"[MatchState] Could not extract valid table number from station name: {station.Attributes.Name}");
                        continue;
                    }

                    // Get the match ID from the station relationship
                    var matchIdStr = station.Relationships?.Match?.Data?.Id;
                    if (string.IsNullOrEmpty(matchIdStr) || !long.TryParse(matchIdStr, out var matchId))
                    {
                        Console.WriteLine($"[MatchState] Station {station.Attributes.Name} has no match assigned");
                        continue;
                    }

                    newAssignments[matchId] = tableNumber;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MatchState] Error processing station: {ex.Message}");
                }
            }

            // Now apply all changes atomically:
            // 1. Clear assignments for matches that were assigned but aren't in new assignments
            foreach (var previousMatch in previouslyAssignedMatches)
            {
                if (!newAssignments.ContainsKey(previousMatch.MatchId))
                {
                    previousMatch.TableNumber = 0;
                    previousMatch.TableAssignedAt = null;
                    Console.WriteLine($"[MatchState] Cleared table assignment for match {previousMatch.MatchId}");
                }
            }

            // 2. Apply new assignments
            foreach (var (matchId, tableNumber) in newAssignments)
            {
                if (_matches.TryGetValue(matchId, out var match))
                {
                    match.TableNumber = tableNumber;
                    match.TableAssignedAt = DateTime.UtcNow;
                    Console.WriteLine($"[MatchState] Auto-assigned match {matchId} ({match.Player1Name} vs {match.Player2Name}) to Table {tableNumber}");
                }
            }

            // Save the auto-populated assignments
            await SaveTableAssignmentsAsync();

            // Fire a single event after all assignments are complete
            TableAssignmentChanged?.Invoke(this, new TableAssignmentChangedEventArgs());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MatchState] Error fetching stations: {ex.Message}");
        }
    }

    /// <summary>
    /// Extract table number from station name (e.g., "Table 1" -> 1, "Stream 1" -> 1)
    /// </summary>
    private int ExtractTableNumber(string stationName)
    {
        if (string.IsNullOrEmpty(stationName))
        {
            return 0;
        }

        // Try to find the last number sequence in the name
        var lastNumberMatch = System.Text.RegularExpressions.Regex.Matches(stationName, @"\d+").LastOrDefault();
        if (lastNumberMatch != null && int.TryParse(lastNumberMatch.Value, out var tableNumber))
        {
            return tableNumber;
        }

        return 0;
    }

    #region Persistence

    private string GetTableAssignmentsPath()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OBBX");
        Directory.CreateDirectory(appDataPath);
        return Path.Combine(appDataPath, "table_assignments.json");
    }

    private async Task SaveTableAssignmentsAsync()
    {
        var assignments = _matches.Values
            .Where(m => m.TableNumber > 0)
            .Select(m => new TableAssignmentRecord
            {
                MatchId = m.MatchId,
                TableNumber = m.TableNumber,
                AssignedAt = m.TableAssignedAt
            })
            .ToList();

        var json = JsonSerializer.Serialize(assignments, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(GetTableAssignmentsPath(), json);
    }

    private async Task LoadTableAssignmentsAsync()
    {
        var path = GetTableAssignmentsPath();
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path);
            var assignments = JsonSerializer.Deserialize<List<TableAssignmentRecord>>(json);

            if (assignments != null)
            {
                foreach (var assignment in assignments)
                {
                    if (_matches.TryGetValue(assignment.MatchId, out var match))
                    {
                        match.TableNumber = assignment.TableNumber;
                        match.TableAssignedAt = assignment.AssignedAt;
                    }
                }
            }
        }
        catch
        {
            // Ignore errors loading assignments
        }
    }

    #endregion

    public void Dispose()
    {
        _refreshTimer?.Dispose();
    }
}

/// <summary>
/// Filter options for querying matches
/// </summary>
public class MatchFilter
{
    public TournamentMatchState? State { get; set; }
    public MatchStage? Stage { get; set; }
    public int? Round { get; set; }
    public bool? HasTableAssigned { get; set; }
    public bool? IsPlayable { get; set; }
    public string? SearchTerm { get; set; }
}

/// <summary>
/// Event args for when matches are updated from Challonge
/// </summary>
public class MatchesUpdatedEventArgs : EventArgs
{
    public IReadOnlyList<TournamentMatch> AddedMatches { get; init; } = new List<TournamentMatch>();
    public IReadOnlyList<TournamentMatch> UpdatedMatches { get; init; } = new List<TournamentMatch>();
    public IReadOnlyList<long> RemovedMatchIds { get; init; } = new List<long>();
}

/// <summary>
/// Event args for when a table assignment changes
/// </summary>
public class TableAssignmentChangedEventArgs : EventArgs
{
    public TournamentMatch? Match { get; init; }
    public int PreviousTableNumber { get; init; }
    public int NewTableNumber { get; init; }
}

/// <summary>
/// Record for persisting table assignments
/// </summary>
internal class TableAssignmentRecord
{
    public long MatchId { get; set; }
    public int TableNumber { get; set; }
    public DateTime? AssignedAt { get; set; }
}
