using LiteDB;
namespace GelbooruBackup.Entities;

public class SyncedToSzurubooruPost
{
    [BsonId]
    public long Id { get; set; }
    public long SzurubooruId { get; set; }
    public int Version { get; set; }
}
