using GelbooruBackup.Entities;
using System.Text.Json;

namespace GelbooruBackup.Gelbooru;
public class GelbooruFavoriteDownloader
{
    private readonly HttpClient _client;
    private readonly string _apiKey;
    private readonly string _userId;
    private readonly int _maxPerPage = 100;
    private readonly int _maxSafePages = 100;
    private int _maxPerSafeRequest => _maxSafePages * _maxPerPage;
    private readonly HashSet<long> _downloadedPostIds = new();
    private readonly List<GelbooruPost> _collectedPosts = new();

    public GelbooruFavoriteDownloader(string apiKey, string userId)
    {
        _client = new HttpClient();
        _apiKey = apiKey;
        _userId = userId;
    }

    public async Task<List<GelbooruPost>> DownloadAllFavoritesAsync()
    {
        Console.WriteLine("🚀 Получение максимального ID...");
        var maxId = await GetMaxPostIdAsync();
        Console.WriteLine($"📌 Максимальный ID: {maxId}");
        await DownloadRangeAsync(0, maxId);
        Console.WriteLine($"✅ Скачивание завершено. Всего постов: {_collectedPosts.Count}");
        return _collectedPosts;
    }

    private async Task<long> GetMaxPostIdAsync()
    {
        string tags = "sort:id:desc";
        string url = $"https://gelbooru.com/index.php?page=dapi&s=post&q=index&limit=1&json=1&tags={Uri.EscapeDataString(tags)}&api_key={_apiKey}&user_id={_userId}";

        try
        {
            var json = await _client.GetStringAsync(url);
            var result = JsonSerializer.Deserialize<GelbooruResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result.Posts.First().Id;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Ошибка при получении максимального ID: {ex.Message}");
            return 12_000_000;
        }
    }

    private async Task DownloadRangeAsync(long minId, long maxId)
    {
        string tags = $"fav:{_userId} id:>={minId} id:<={maxId}";
        int count = await CountPostsAsync(tags);
        await Task.Delay(100);
        if (count == 0)
        {
            Console.WriteLine($"⛔ Нет постов в диапазоне {minId}-{maxId}");
            return;
        }

        if (count <= _maxPerSafeRequest)
        {
            Console.WriteLine($"⬇️ Скачиваем {count} постов ({minId}–{maxId})");
            await DownloadPagesAsync(tags);
        }
        else
        {
            long mid = minId + (maxId - minId) / 2; // безопасный mid
            await DownloadRangeAsync(minId, mid);
            await DownloadRangeAsync(mid + 1, maxId);
        }
    }

    private async Task<int> CountPostsAsync(string tags)
    {
        try
        {
            string url = $"https://gelbooru.com/index.php?page=dapi&s=post&q=index&limit=0&json=1&tags={Uri.EscapeDataString(tags)}&api_key={_apiKey}&user_id={_userId}";
            var json = await _client.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return root.GetProperty("@attributes").GetProperty("count").GetInt32();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Ошибка при подсчёте: {ex.Message}");
            return 0;
        }
    }

    private async Task DownloadPagesAsync(string tags)
    {
        var tasks = new List<Task>();
        for (int pid = 0; pid < _maxSafePages; pid++)
        {
            int page = pid;
            await Task.Delay(20*pid); // задержка между задачами (уменьшает шанс 429)
            tasks.Add(Task.Run(async () =>
            {
                string url = $"https://gelbooru.com/index.php?page=dapi&s=post&q=index&limit=100&pid={page}&json=1&tags={Uri.EscapeDataString(tags)}&api_key={_apiKey}&user_id={_userId}";

                var posts = await DownloadPageWithRetryAsync(url);
                if (posts == null) return;

                foreach (var post in posts)
                {
                    lock (_downloadedPostIds)
                    {
                        if (_downloadedPostIds.Contains(post.Id)) continue;
                        _downloadedPostIds.Add(post.Id);
                        _collectedPosts.Add(post);
                    }
                    // Console.WriteLine($"📥 Пост {post.Id} получен");
                }
            }));
        }

        await Task.WhenAll(tasks);
    }

    private async Task<List<GelbooruPost>> DownloadPageWithRetryAsync(string url, int maxRetries = 3)
    {
        int tries = 0;
        while (tries < maxRetries)
        {
            try
            {
                var response = await _client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<GelbooruResponse>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return result?.Posts ?? new List<GelbooruPost>();
                }
                else if ((int)response.StatusCode == 429)
                {
                    Console.WriteLine($"⚠️ Too many requests, ждем... Попытка {tries + 1}");
                    await Task.Delay(1000 * (tries + 1)); // нарастающая задержка
                }
                else
                {
                    Console.WriteLine($"⚠️ Ошибка HTTP {response.StatusCode} для {url}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка при запросе {url}: {ex.Message}");
                await Task.Delay(500 * (tries + 1));
            }
            tries++;
        }
        Console.WriteLine($"❌ Не удалось загрузить страницу после {maxRetries} попыток: {url}");
        return null;
    }
}
