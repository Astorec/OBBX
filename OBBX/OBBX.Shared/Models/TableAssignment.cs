namespace OBBX.Shared.Models;

public class TableAssignment
{
    public int? TableNumber { get; set; }
    public string Player1Name { get; set; } = "";
    public string Player2Name { get; set; } = "";
    public long MatchId { get; set; }
}