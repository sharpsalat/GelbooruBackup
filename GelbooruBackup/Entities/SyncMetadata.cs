using GelbooruBackup.Entities.Interfaces;
using LiteDB;
namespace GelbooruBackup.Entities;

public class SyncMetadata : ILiteDbEntity
{
    public const string MetaDataStaticId = "sync_metadata";
    public static string TableName => "sync_metadata";
    [BsonId]
    public string Id { get; set; } = MetaDataStaticId; // фиксированный ID для одной записи
    public int FavoritesCount { get; set; }
    public DateTime LastSyncedAt { get; set; }
}
