using GelbooruBackup.Entities;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GelbooruBackup.Gelbooru;
public class GelbooruFavoriteDownloader
{
    private readonly HttpClient _client;
    private readonly string _apiKey;
    private readonly string _userId;
    private readonly string _favouritesOwnerId;
    private readonly HashSet<long> _downloadedPostIds = new();
    private readonly List<GelbooruPost> _collectedPosts = new();

    public GelbooruFavoriteDownloader(string apiKey, string userId, string favouritesOwnerId)
    {
        _client = GelbooruClient.HttpClient;
        _apiKey = apiKey;
        _userId = userId;
        _favouritesOwnerId = favouritesOwnerId;
    }

    private bool HasNewPosts(List<GelbooruPost> posts, List<long> existingPostIds)
    {
        return posts.Any(p => !existingPostIds.Contains(p.Id));
    }

    public async Task<List<GelbooruPost>> DownloadNewFavoritesAsync(List<long> existingPostIds)
    {
        await DownloadAllAsync(existingPostIds);
        Console.WriteLine($"✅ Скачивание завершено. Всего постов: {_collectedPosts.Count}");
        return _collectedPosts;
    }

    public async Task<List<GelbooruPost>> DownloadAllFavoritesAsync()
    {
        await DownloadAllAsync();
        Console.WriteLine($"✅ Скачивание завершено. Всего постов: {_collectedPosts.Count}");
        return _collectedPosts;
    }

    private async Task DownloadAllAsync(List<long> existingPostIds = null)
    {
        int pid = 0;
        int previousPid = 0;
        bool lastPageHasNewPosts = true;
        do
        {
            Console.WriteLine($"pid: {pid}");
            int page = pid;
            await Task.Delay(1000);
            await Task.Run(async () =>
            {
                string url = $"https://gelbooru.com/index.php?page=favorites&s=view&id={_favouritesOwnerId}&pid={page}";

                var posts = await DownloadPageWithRetryAsync(url);
                if (posts == null) return;

                if (existingPostIds != null)
                    lastPageHasNewPosts = HasNewPosts(posts, existingPostIds);

                previousPid = pid;
                pid += posts.Count;

                foreach (var post in posts)
                {
                    lock (_downloadedPostIds)
                    {
                        if (_downloadedPostIds.Contains(post.Id)) continue;
                        _downloadedPostIds.Add(post.Id);
                        _collectedPosts.Add(post);
                    }
                }
            });
        }
        while (pid != previousPid && lastPageHasNewPosts);
    }

    public static List<long> ExtractPostIdsFromHtml(string htmlContent)
    {
        var postIds = new List<long>();

        // Ищем все вхождения posts[ЧИСЛО]
        var matches = Regex.Matches(htmlContent, @"posts\[(\d+)\]");

        foreach (Match match in matches)
        {
            if (long.TryParse(match.Groups[1].Value, out long postId))
            {
                // Добавляем только уникальные ID
                if (!postIds.Contains(postId))
                {
                    postIds.Add(postId);
                }
            }
        }

        return postIds;
    }

    private async Task<List<GelbooruPost>> GetPostsByHtmlWithSemaphoreAsync(string html)
    {
        const int maxParallel = 5;
        const int delayBetweenStartMs = 100;

        var posts = new ConcurrentBag<GelbooruPost>();
        var semaphore = new SemaphoreSlim(maxParallel);
        var tasks = new List<Task>();

        var ids = ExtractPostIdsFromHtml(html);
        foreach (var id in ids)
        {
            await Task.Delay(delayBetweenStartMs);

            await semaphore.WaitAsync();

            var task = Task.Run(async () =>
            {
                try
                {
                    var postUrl = $"https://gelbooru.com/index.php?page=dapi&s=post&q=index&id={id}&json=1&api_key={_apiKey}&user_id={_userId}";
                    var postsById = await DownloadPageWithRetryAsync(postUrl);
                    if (postsById != null)
                        posts.Add(postsById.First());
                }
                catch (Exception ex) 
                {
                    throw ex;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            tasks.Add(task);
        }

        await Task.WhenAll(tasks);

        return posts.ToList();
    }

    private async Task<List<GelbooruPost>> GetPostsByHtmlAsync(string html)
    {
        var ids = ExtractPostIdsFromHtml(html);
        var posts = new List<GelbooruPost>();
        foreach (var id in ids)
        {
            await Task.Delay(50); // небольшая задержка между запросами
            var postUrl = $"https://gelbooru.com/index.php?page=dapi&s=post&q=index&id={id}&json=1&api_key={_apiKey}&user_id={_userId}";
            var postsById = await DownloadPageWithRetryAsync(postUrl);
            posts.AddRange(postsById);
        }
        return posts;
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
                    var content = await response.Content.ReadAsStringAsync();
                    if (content.Contains("<!DOCTYPE html"))
                    {
                        return await GetPostsByHtmlAsync(content);
                        //return await GetPostsByHtmlWithSemaphoreAsync(content);
                    }
                    else
                    {
                        var result = JsonSerializer.Deserialize<GelbooruResponse>(content, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                        return result?.Posts ?? new List<GelbooruPost>();
                    }
                }
                else if ((int)response.StatusCode == 429)
                {
                    Console.WriteLine($"⚠️ Too many requests, ждем... Попытка {tries + 1}");
                    await Task.Delay(1000 * (tries + 1)); // нарастающая задержка
                }
                else
                {
                    Console.WriteLine($"⚠️ Ошибка HTTP {response.StatusCode} для {url}");
                    return null; //TODO: add retry if timed out?
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
