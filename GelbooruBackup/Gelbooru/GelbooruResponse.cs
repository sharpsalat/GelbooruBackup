using GelbooruBackup.Entities;
using System.Text.Json.Serialization;
namespace GelbooruBackup.Gelbooru;

public class GelbooruResponse
{
    [JsonPropertyName("@attributes")]
    public GelbooruAttributes Attributes { get; set; }

    [JsonPropertyName("post")]
    public List<GelbooruPost> Posts { get; set; }
}
