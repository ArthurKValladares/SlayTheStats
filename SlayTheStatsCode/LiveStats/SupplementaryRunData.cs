using System.Text.Json.Serialization;

namespace SlayTheStats.SlayTheStatsCode.LiveStats;

/// <summary>Data saved for a single run in our supplementary stats files.</summary>
public class SupplementaryRunData
{
    [JsonPropertyName("ascension")]
    public int Ascension { get; set; }

    [JsonPropertyName("build_id")]
    public string BuildId { get; set; } = "";

    /// <summary>Key = card ModelId entry string (e.g. "CARD.CLAW").</summary>
    [JsonPropertyName("card_stats")]
    public Dictionary<string, CardDrawPlayData> CardStats { get; set; } = new();
}
