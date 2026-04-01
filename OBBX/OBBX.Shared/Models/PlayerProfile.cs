public class PlayerDeckProfile
{
    /// <summary>
    /// The player's name as registered in the tournament
    /// </summary>
    public string WBOUsername {get; set;}
    public DeckProfile Deck { get; set; } = new DeckProfile();
}