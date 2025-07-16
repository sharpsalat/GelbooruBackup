using LiteDB;
namespace GelbooruBackup.Entities;

public class SyncedToSzurubooruTag
{
    [BsonId]
    public string Name { get; set; }  // имя — уникальный идентификатор
}
