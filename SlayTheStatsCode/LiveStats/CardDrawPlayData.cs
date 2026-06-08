using System.Text.Json.Serialization;

namespace SlayTheStats.SlayTheStatsCode.LiveStats;

public class CardDrawPlayData
{
    [JsonPropertyName("drawn")]
    public int Drawn { get; set; }

    [JsonPropertyName("played")]
    public int Played { get; set; }
}
