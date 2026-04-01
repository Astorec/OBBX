namespace OBBX.Shared.Models;

public class AppSettings
{
    public ChallongeSettings Challonge {get; set;} = new ChallongeSettings();
    public OBSWebSocketSettings OBSWebSocket {get; set;} = new OBSWebSocketSettings();
    public CsvImportSettings CsvImport {get; set;} = new CsvImportSettings();
    public TableSettings Tables {get; set;} = new TableSettings();
}

public class ChallongeSettings
{
    public string? Username {get; set;} = string.Empty;
    public string? ApiKey {get; set;} = string.Empty;
    public string? Uri {get; set;} = string.Empty;
    public int RefreshIntervalSeconds {get; set;} = 30;
    public bool IsDoubleElim {get; set;} = false;
    public int CurrentRound {get; set; } = 0;
    public int CurrentLoserBracket {get; set;} = -1; // Loser Bracket gets set to -1 and lower so we can get it that way
    public string CurrentStage {get; set;} = "Unknown";
    public string CurrentBracket {get; set;} = "NotDoubleElim";
    public int playerCount {get; set; } = 0;
}

public class OBSWebSocketSettings
{
    public string? Address {get; set;} = string.Empty;
    public int Port {get; set;} = 4444;
    public string? Key {get; set;} = string.Empty;
}

public class CsvImportSettings
{
    public string? ImportPath {get; set;} = string.Empty;
    public bool IsBrowserUpload {get; set;} = false;
    public string? BrowserFileName {get; set;} = string.Empty;
}

public class TableSettings
{
    public int TableCount {get; set;} = 4;
    public int LiveFeedTableNumber {get; set;} = 1;
    public bool UseChallongeForTables{get; set;} = false;
}