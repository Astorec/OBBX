namespace OBBX.Shared.Models;

/// <summary>
/// Information about the current bracket format based on player count
/// </summary>
public class BracketInfo
{
    public int PlayerCount { get; set; }
    public BracketType BracketType { get; set; }
    public string BracketTypeName => BracketType switch
    {
        BracketType.RoundRobin => "Round Robin",
        BracketType.GroupRoundRobin => "Group Round Robin",
        BracketType.GroupSwiss => "Group Swiss",
        BracketType.DoubleElimination => "Double Elimination",
        BracketType.SingleElimination => "Single Elimination",
        _ => "Unknown"
    };
    public string StageName { get; set; } = "First Stage";
    public string PointsFormat { get; set; } = "First to Four Points";
    
    /// <summary>
    /// Detect bracket type based on player count
    /// </summary>
    public static BracketInfo FromPlayerCount(int playerCount)
    {
        var info = new BracketInfo { PlayerCount = playerCount };
        
        info.BracketType = playerCount switch
        {
            >= 120 => BracketType.SingleElimination,
            >= 64 => BracketType.DoubleElimination,
            >= 17 => BracketType.GroupSwiss,
            >= 9 => BracketType.GroupRoundRobin,
            >= 4 => BracketType.RoundRobin,
            _ => BracketType.Unknown
        };
        
        return info;
    }
}

public enum BracketType
{
    Unknown,
    RoundRobin,
    GroupRoundRobin,
    GroupSwiss,
    DoubleElimination,
    SingleElimination
}
