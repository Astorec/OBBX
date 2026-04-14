namespace OBBX.Shared.Models;

public class TableAssignment
{
    public int? TableNumber { get; set; }
    public string Player1Name { get; set; } = "";
    public string Player2Name { get; set; } = "";
    public int player1Score { get; set; } = 0;
    public int player2Score { get; set; } = 0;
    public long MatchId { get; set; }
}