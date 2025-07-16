using System.Text.Json.Serialization;
namespace GelbooruBackup.Entities;

public class GelbooruTagResponse
{
    [JsonPropertyName("tag")]
    public List<GelbooruTag> Tags { get; set; }
}