using GelbooruBackup.Entities.Interfaces;
using LiteDB;
namespace GelbooruBackup.Entities;

public class SyncedToSzurubooruPost : ILiteDbEntity
{
    public static string TableName => "synced_posts";
    [BsonId]
    public long Id { get; set; }
    public long SzurubooruId { get; set; }
    public int Version { get; set; }
}
