namespace OBBX.Shared.Models;

public class AppSettings
{
    public ChallongeSettings Challonge {get; set;} = new ChallongeSettings();
    public OBSWebSocketSettings OBSWebSocket {get; set;} = new OBSWebSocketSettings();
    public CsvImportSettings CsvImport {get; set;} = new CsvImportSettings();
    
}

public class ChallongeSettings
{
    public string? Username {get; set;} = string.Empty;
    public string? ApiKey {get; set;} = string.Empty;
    public string? Uri {get; set;} = string.Empty;
    public int RefreshIntervalSeconds {get; set;} = 30;
}

public class OBSWebSocketSettings
{
    public string? Address {get; set;} = string.Empty;
    public int Port {get; set;} = 4444;
    public string? Key {get; set;} = string.Empty;
}

public class CsvImportSettings
{
    public string? LastImportPath {get; set;} = string.Empty;
}