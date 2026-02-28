namespace OBBX.Shared.Models;

/// <summary>
/// Represents a participant's standing in the tournament
/// </summary>
public class ParticipantStanding
{
    public long ParticipantId { get; set; }
    public string Name { get; set; } = "";
    public int Seed { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int MatchesPlayed { get; set; }
    public int Points => Wins; 
    
    public string Record => $"{Wins}-{Losses}";
    public double WinRate => MatchesPlayed > 0 ? (double)Wins / MatchesPlayed * 100 : 0;
}
