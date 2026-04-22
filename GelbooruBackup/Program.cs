using System.Text;
using System.Text.Json;

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

            Console.WriteLine($"Application started with parameters:\n{config.ToString()}");
            var planner = new Planner(config);
            planner.Start();
            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                Console.WriteLine($"Application is shutting down...");
                planner.StopAsync().GetAwaiter().GetResult();
                Console.WriteLine($"Application shutdown completed successfully");
            };
            await Task.Delay(Timeout.Infinite);
            Console.WriteLine("Application terminated unexpectedly");
        }

        public static Config LoadConfigFromEnv()
        {
            var shortSyncTimeout = EnvHelper.GetOptionalIntEnv("SHORT_SYNC_TIMEOUT");
            var fullSyncTimeout = EnvHelper.GetOptionalIntEnv("FULL_SYNC_TIMEOUT");
            var backendHost = EnvHelper.GetOptionalStringEnv("BACKEND_HOST");
            var gelbooruUserId = EnvHelper.GetRequiredEnv("GELBOORU_USER_ID");
            var favouritesOwnerId = Environment.GetEnvironmentVariable("FAVOURITES_OWNER_ID");

            // FullSyncOnStartup is optional; Planner treats null as default true.
            var fullSyncOnStartup = EnvHelper.GetOptionalBoolEnv("FULL_SYNC_ON_STARTUP");

            return new Config
            {
                GelbooruApiKey = EnvHelper.GetRequiredEnv("GELBOORU_API_KEY"),
                GelbooruUserId = gelbooruUserId,
                FavouritesOwnerId = string.IsNullOrEmpty(favouritesOwnerId) ? gelbooruUserId : favouritesOwnerId,
                GelbooruUsername = EnvHelper.GetRequiredEnv("GELBOORU_USERNAME"),
                GelbooruPassword = EnvHelper.GetRequiredEnv("GELBOORU_PASSWORD"),
                SzurubooruURL = backendHost != null ? $"http://{backendHost}:6666" : EnvHelper.GetRequiredEnv("SZURUBOORU_URL"),
                SzurubooruUserName = EnvHelper.GetRequiredEnv("SZURUBOORU_USER_NAME"),
                SzurubooruUserPassword = EnvHelper.GetRequiredEnv("SZURUBOORU_USER_PASSWORD"),
                FilesFolderPath = Environment.GetEnvironmentVariable("FILES_FOLDER_PATH") ?? Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory)!, "data"),

                ShortSyncTimeout = shortSyncTimeout ?? 60,
                FullSyncTimeout = fullSyncTimeout ?? 10800,
                FullSyncOnStartup = fullSyncOnStartup ?? true,
            };
        }

        public static async Task<Config> LoadParamsFromJsonAsync(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Configuration file not found: {path}");

            string json = await File.ReadAllTextAsync(path);
            Config config = JsonSerializer.Deserialize<Config>(json);

            if (config == null)
                throw new InvalidOperationException("Failed to deserialize JSON to Config object.");

            return config;
        }
    }
}