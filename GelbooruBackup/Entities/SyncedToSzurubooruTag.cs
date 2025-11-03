using GelbooruBackup.Entities.Interfaces;
using LiteDB;
namespace GelbooruBackup.Entities;

public class SyncedToSzurubooruTag : ILiteDbEntity
{
    public static string TableName => "synced_tags";

    [BsonId]
    public string Name { get; set; }  // имя — уникальный идентификатор
}
