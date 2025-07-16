namespace GelbooruBackup.Entities;

public class SzurubooruUser
{
    public string name { get; set; }
    public DateTime creationTime { get; set; }
    public object lastLoginTime { get; set; }
    public int version { get; set; }
    public string rank { get; set; }
    public string avatarStyle { get; set; }
    public string avatarUrl { get; set; }
    public int commentCount { get; set; }
    public int uploadedPostCount { get; set; }
    public int favoritePostCount { get; set; }
    public bool likedPostCount { get; set; }
    public bool dislikedPostCount { get; set; }
    public object email { get; set; }
}