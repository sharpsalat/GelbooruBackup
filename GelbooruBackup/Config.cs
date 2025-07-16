using System.Reflection;

namespace GelbooruBackup
{
    public class Config
    {
        public string GelbooruApiKey { get; set; }
        public string GelbooruUserId { get; set; }
        public string SzurubooruURL { get; set; }
        public string SzurubooruUserName { get; set; }
        public string SzurubooruUserPassword { get; set; }
        public string FilesFolderPath { get; set; }
        public int ShortSyncTimeout { get; set; }
        public int FullSyncTimeout { get; set; }
        public override string ToString()
        {
            var properties = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            return string.Join(Environment.NewLine,
                Array.ConvertAll(properties,
                    prop => $"{prop.Name}: {prop.GetValue(this)}"));
        }
    }
}