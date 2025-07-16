namespace GelbooruBackup.Szurubooru;

public class SzurubooruTagInfo
{
    public List<string> names { get; set; }
    public string category { get; set; }
    public int version { get; set; }
    public string description { get; set; }
    public DateTimeOffset? creationTime { get; set; }
    public DateTimeOffset? lastEditTime { get; set; }
    public int usages { get; set; }
    public List<string> suggestions { get; set; }
    public List<string> implications { get; set; }
}
