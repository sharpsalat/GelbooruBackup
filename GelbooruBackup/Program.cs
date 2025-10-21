using GelbooruBackup.Gelbooru;
using GelbooruBackup.Szurubooru;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace GelbooruBackup
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

#if DEBUG
            var config = await LoadParamsFromJsonAsync("config.json");
#else
            var config = LoadConfigFromEnv();
#endif

            Console.WriteLine($"Приложение запущено с параметрами:\n{config.ToString()}");
            var planner = new Planner(config);
            planner.Start();
            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                Console.WriteLine($"Приложение завершает работу...");
                planner.StopAsync().GetAwaiter().GetResult();
                Console.WriteLine($"Приложение успешно завершило работу");
            };
            await Task.Delay(Timeout.Infinite);
            Console.WriteLine("Приложение неожиданно завершило работу");
        }

        public static Config LoadConfigFromEnv()
        {
            string GetRequiredEnv(string name)
            {
                var value = Environment.GetEnvironmentVariable(name);
                if (string.IsNullOrWhiteSpace(value))
                    throw new InvalidOperationException($"Environment variable '{name}' is required but was not set.");
                return value;
            }
            var shortSyncTimeoutString = Environment.GetEnvironmentVariable("SHORT_SYNC_TIMEOUT");
            var fullSyncTimeoutString = Environment.GetEnvironmentVariable("FULL_SYNC_TIMEOUT");
            var backendHost = Environment.GetEnvironmentVariable("BACKEND_HOST");
            var gelbooruUserId = GetRequiredEnv("GELBOORU_USER_ID");
            var favouritesOwnerId = Environment.GetEnvironmentVariable("FAVOURITES_OWNER_ID");
            return new Config
            {
                GelbooruApiKey = GetRequiredEnv("GELBOORU_API_KEY"),
                GelbooruUserId = gelbooruUserId,
                FavouritesOwnerId = string.IsNullOrEmpty(favouritesOwnerId) ? gelbooruUserId : favouritesOwnerId,
                GelbooruUsername = GetRequiredEnv("GELBOORU_USERNAME"),
                GelbooruPassword = GetRequiredEnv("GELBOORU_PASSWORD"),
                SzurubooruURL = backendHost != null ? $"http://{backendHost}:6666" : GetRequiredEnv("SZURUBOORU_URL"),
                SzurubooruUserName = GetRequiredEnv("SZURUBOORU_USER_NAME"),
                SzurubooruUserPassword = GetRequiredEnv("SZURUBOORU_USER_PASSWORD"),
                FilesFolderPath = Environment.GetEnvironmentVariable("FILES_FOLDER_PATH") ?? Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory)!, "data"),
                
                ShortSyncTimeout = shortSyncTimeoutString != null ? int.Parse(shortSyncTimeoutString) : 60,
                FullSyncTimeout = fullSyncTimeoutString != null ? int.Parse(fullSyncTimeoutString) : 10800,
            };
        }
        public static async Task<Config> LoadParamsFromJsonAsync(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Файл конфигурации не найден: {path}");

            string json = await File.ReadAllTextAsync(path);
            Config config = JsonSerializer.Deserialize<Config>(json);

            if (config == null)
                throw new InvalidOperationException("Не удалось десериализовать JSON в объект Params.");

            return config;
        }
    }
}