using LiteDB;
namespace GelbooruBackup.Entities;

public class SyncMetadata
{
    [BsonId]
    public string Id { get; set; } = "sync_metadata"; // фиксированный ID для одной записи
    public int FavoritesCount { get; set; }
    public DateTime LastSyncedAt { get; set; }
}
