namespace OBBX.Shared.Models;

/// <summary>
/// Represents a match with all relevant data from Challonge plus local table assignment.
/// Designed to handle dynamically generated matches in Swiss/Elimination brackets.
/// </summary>
public class TournamentMatch
{
    /// <summary>
    /// Unique match ID from Challonge
    /// </summary>
    public long MatchId { get; set; }

    /// <summary>
    /// Tournament ID this match belongs to
    /// </summary>
    public long TournamentId { get; set; }

    /// <summary>
    /// Current round number (negative for losers bracket in double elimination)
    /// </summary>
    public int Round { get; set; }

    /// <summary>
    /// Suggested play order from Challonge
    /// </summary>
    public int? SuggestedPlayOrder { get; set; }

    /// <summary>
    /// Player 1's participant ID (null if not yet determined, e.g., waiting for previous match)
    /// </summary>
    public long? Player1Id { get; set; }

    /// <summary>
    /// Player 1's display name (null if player not yet determined)
    /// </summary>
    public string? Player1Name { get; set; }

    /// <summary>
    /// Player 2's participant ID (null if not yet determined)
    /// </summary>
    public long? Player2Id { get; set; }

    /// <summary>
    /// Player 2's display name (null if player not yet determined)
    /// </summary>
    public string? Player2Name { get; set; }

    /// <summary>
    /// Current state of the match
    /// </summary>
    public TournamentMatchState State { get; set; } = TournamentMatchState.Pending;

    /// <summary>
    /// Winner's participant ID (null if match not complete)
    /// </summary>
    public long? WinnerId { get; set; }

    /// <summary>
    /// Loser's participant ID (null if match not complete)
    /// </summary>
    public long? LoserId { get; set; }

    /// <summary>
    /// Scores in CSV format (e.g., "2-1,3-2")
    /// </summary>
    public string? ScoresCsv { get; set; }

    /// <summary>
    /// Assigned table number. 0 = unassigned, null = not applicable
    /// </summary>
    public int TableNumber { get; set; } = 0;

    /// <summary>
    /// When this match data was last updated from Challonge
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the table was assigned (null if not assigned)
    /// </summary>
    public DateTime? TableAssignedAt { get; set; }

    /// <summary>
    /// Optional identifier for the match (e.g., "A1", "B2", bracket position)
    /// </summary>
    public string? Identifier { get; set; }

    /// <summary>
    /// Group ID for round robin groups (null if not applicable)
    /// </summary>
    public long? GroupId { get; set; }

    /// <summary>
    /// The stage of the tournament this match belongs to
    /// </summary>
    public MatchStage Stage { get; set; } = MatchStage.Unknown;

    /// <summary>
    /// Whether this match is ready to be played (both players known and match is open/pending)
    /// </summary>
    public bool IsPlayable => Player1Id.HasValue && Player2Id.HasValue && State == TournamentMatchState.Open;

    /// <summary>
    /// Whether a table has been assigned
    /// </summary>
    public bool HasTableAssigned => TableNumber > 0;

    /// <summary>
    /// Display string for the match (handles null players)
    /// </summary>
    public string DisplayName => $"{Player1Name ?? "TBD"} vs {Player2Name ?? "TBD"}";
}

/// <summary>
/// Represents the current state of a match
/// </summary>
public enum TournamentMatchState
{
    /// <summary>
    /// Match is waiting for players to be determined
    /// </summary>
    Pending,

    /// <summary>
    /// Match is open and ready to be played
    /// </summary>
    Open,

    /// <summary>
    /// Match is currently in progress
    /// </summary>
    InProgress,

    /// <summary>
    /// Match has been completed
    /// </summary>
    Complete
}

/// <summary>
/// Represents the stage/phase of the tournament a match belongs to
/// </summary>
public enum MatchStage
{
    /// <summary>
    /// Stage could not be determined
    /// </summary>
    Unknown,

    /// <summary>
    /// Group stage / pools / round robin phase
    /// </summary>
    GroupStage,

    /// <summary>
    /// Finals / bracket / elimination phase (after groups)
    /// </summary>
    Finals
}
