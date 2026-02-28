namespace OBBX.Shared.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Represents a station (table) from Challonge API v2.1
/// </summary>
public class Station
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("attributes")]
    public StationAttributes Attributes { get; set; } = new();

    [JsonPropertyName("relationships")]
    public StationRelationships? Relationships { get; set; }
}

public class StationAttributes
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("stream_url")]
    public string? StreamUrl { get; set; }

    [JsonPropertyName("details")]
    public string? Details { get; set; }

    [JsonPropertyName("details_format")]
    public string? DetailsFormat { get; set; }
}

public class StationRelationships
{
    [JsonPropertyName("match")]
    public StationMatchRelationship? Match { get; set; }

    [JsonPropertyName("station_queuers")]
    public StationQueuerRelationship? StationQueuers { get; set; }
}

public class StationMatchRelationship
{
    [JsonPropertyName("data")]
    public StationMatchData? Data { get; set; }

    [JsonPropertyName("links")]
    public MatchLinks? Links { get; set; }
}

public class StationMatchData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
}

public class MatchLinks
{
    [JsonPropertyName("related")]
    public string? Related { get; set; }
}

public class StationQueuerRelationship
{
    [JsonPropertyName("data")]
    public List<object> Data { get; set; } = new();

    [JsonPropertyName("links")]
    public QueuerLinks? Links { get; set; }
}

public class QueuerLinks
{
    [JsonPropertyName("related")]
    public string? Related { get; set; }

    [JsonPropertyName("meta")]
    public QueuerMeta? Meta { get; set; }
}

public class QueuerMeta
{
    [JsonPropertyName("count")]
    public int Count { get; set; }
}

/// <summary>
/// Response wrapper for stations API call
/// </summary>
public class StationsResponse
{
    [JsonPropertyName("data")]
    public List<Station> Data { get; set; } = new();

    [JsonPropertyName("included")]
    public List<object>? Included { get; set; }

    [JsonPropertyName("meta")]
    public ResponseMeta? Meta { get; set; }

    [JsonPropertyName("links")]
    public ResponseLinks? Links { get; set; }
}

public class ResponseMeta
{
    [JsonPropertyName("count")]
    public int Count { get; set; }
}

public class ResponseLinks
{
    [JsonPropertyName("self")]
    public string? Self { get; set; }

    [JsonPropertyName("next")]
    public string? Next { get; set; }

    [JsonPropertyName("prev")]
    public string? Prev { get; set; }
}

/// <summary>
/// Response wrapper for tournament API call to get the numeric ID
/// </summary>
public class TournamentResponse
{
    [JsonPropertyName("data")]
    public Tournament Data { get; set; } = new();
}

public class Tournament
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("attributes")]
    public TournamentAttributes Attributes { get; set; } = new();
}

public class TournamentAttributes
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
