using GelbooruBackup.Entities.Interfaces;
using LiteDB;
namespace GelbooruBackup.Entities;

public class PostDocument : ILiteDbEntity
{
    public static string TableName => "posts";
    [BsonId]
    public long Id { get; set; }
    public List<string> Tags { get; set; }
    public string Rating { get; set; }
    public string FileUrl { get; set; }
    public string LocalPath { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Owner { get; set; }
    public string Source { get; set; }
    public string CreatedAt { get; set; }
    public string Md5 { get; set; }
    public string Status { get; set; }
    public bool HasComments { get; set; }
    public bool HasNotes { get; set; }
    public int Version { get; set; }
}
