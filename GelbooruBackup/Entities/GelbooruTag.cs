using GelbooruBackup.Entities.Interfaces;
using LiteDB;
namespace GelbooruBackup.Entities;

public class GelbooruTag : ILiteDbEntity
{
    public static string TableName => "tags";
    [BsonId]
    public string Name { get; set; }  // имя — уникальный идентификатор
    public int Id { get; set; }
    public int Count { get; set; }
    public int Type { get; set; }
}
