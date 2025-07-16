using System.Text.Json.Serialization;
namespace GelbooruBackup.Entities;

public class GelbooruAttributes
{
    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }
}
