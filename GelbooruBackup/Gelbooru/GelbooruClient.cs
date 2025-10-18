using GelbooruBackup.Entities;
using LiteDB;
using System.Collections.Concurrent;
using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;
namespace GelbooruBackup.Gelbooru;
public class GelbooruClient
{
    private CancellationTokenSource _cts;
    private const int RequestIntervalTimeOut = 1500;
    private const int MaxRequestsPerBatch = 6;
    private static readonly HttpClient _httpClient = new HttpClient();

    public GelbooruClient(CancellationTokenSource cts)
    {
        _cts = cts;
    }

    private string GetPostsApiUrl(string apikey, string user_id, int page) => $@"https://gelbooru.com/index.php?page=dapi&s=post&q=index&pid={page}&json=1&tags=fav:{user_id}&api_key={apikey}&user_id={user_id}";
    private string GetTagsApiUrl(string apikey, string user_id, string namesParam) => $@"https://gelbooru.com/index.php?page=dapi&s=tag&q=index&json=1&names={namesParam}&json=1&api_key={apikey}&user_id={user_id}";
    public async Task<List<GelbooruPost>> GetFavoritePostsAsync(string apikey, string userName, int page)
    {
        var url = GetPostsApiUrl(apikey, userName, page);
        try
        {
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var result = JsonSerializer.Deserialize<GelbooruResponse>(content, options);

            return result?.Posts ?? new List<GelbooruPost>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка на странице {page}: {ex.Message}");
            return new List<GelbooruPost>();
        }
    }
    public async Task<List<GelbooruPost>> GetAllFavoritePostsAsync(string apiKey, string userId, int expectedTotal)
    {
        const int pageSize = 100;
        const int maxParallel = 5;
        const int maxPerSecond = 10;
        const int retryDelay = 3000;
        const int delayBetweenStartMs = 100;

        int pageCount = (expectedTotal / pageSize) + 2; // +1 запас, +1 на всякий случай

        var allPosts = new ConcurrentBag<GelbooruPost>();
        var semaphore = new SemaphoreSlim(maxParallel);

        var tasks = new List<Task>();

        for (int page = 0; page < pageCount; page++)
        {
            await Task.Delay(delayBetweenStartMs);

            await semaphore.WaitAsync();
            var currentPage = page;

            var task = Task.Run(async () =>
            {
                try
                {
                    List<GelbooruPost>? posts = null;
                    int retryCount = 0;

                    do
                    {
                        try
                        {
                            posts = await GetFavoritePostsAsync(apiKey, userId, currentPage);
                        }
                        catch (HttpRequestException ex) when (ex.Message.Contains("429"))
                        {
                            Console.WriteLine($"⚠ 429 Too Many Requests — страница {currentPage}, повтор через {retryDelay} мс...");
                            await Task.Delay(retryDelay);
                            retryCount++;
                        }
                    } while (posts == null && retryCount < 3);

                    if (posts == null)
                    {
                        Console.WriteLine($"❌ Страница {currentPage} не загружена.");
                        return;
                    }

                    foreach (var post in posts)
                        allPosts.Add(post);

                    Console.WriteLine($"📥 Страница {currentPage} — {posts.Count} постов");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠ Ошибка при загрузке стр. {currentPage}: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            });

            tasks.Add(task);

            // Ограничение в 10 запросов/сек
            if ((page + 1) % maxPerSecond == 0)
            {
                await Task.WhenAll(tasks);
                tasks.Clear();
                await Task.Delay(1000); // Пауза между "пакетами"
            }
        }

        await Task.WhenAll(tasks);
        Console.WriteLine($"✅ Всего загружено: {allPosts.Count} постов (ожидалось: ~{expectedTotal})");
        return allPosts
            .GroupBy(p => p.Id) // удалим дубликаты по ID
            .Select(g => g.First())
            .ToList();
    }
    public async Task<int> GetFavoritePostCountAsync(string apikey, string userName)
    {
        var url = GetPostsApiUrl(apikey, userName, page: 0); // любая страница, count всегда один
        Console.WriteLine($"Запрос количества избранного -> URL: {url}");

        try
        {
            using var response = await _httpClient.GetAsync(url);
            Console.WriteLine($"Ответ от сервера: {(int)response.StatusCode} {response.ReasonPhrase}");
            Console.WriteLine($"  Content-Type: {response.Content.Headers.ContentType?.MediaType ?? "<unknown>"}");

            if (response.Headers.TryGetValues("Retry-After", out var retryValues))
            {
                Console.WriteLine($"  Retry-After: {string.Join(", ", retryValues)}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var snippet = string.IsNullOrEmpty(content) ? "<empty>" : (content.Length > 1000 ? content.Substring(0, 1000) + "..." : content);
            Console.WriteLine($"  Response length: {content?.Length ?? 0}, starts with: '{(string.IsNullOrEmpty(content) ? "none" : content[0].ToString())}'");

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Ошибка при получении количества — HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                Console.WriteLine($"  Snippet: {snippet}");
                await Task.Delay(RequestIntervalTimeOut);
                return 0;
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            try
            {
                var result = JsonSerializer.Deserialize<GelbooruResponse>(content, options);
                var count = result?.Attributes?.Count ?? 0;
                Console.WriteLine($"Успешно распарсено JSON — найдено: {count}");
                await Task.Delay(RequestIntervalTimeOut);
                return count;
            }
            catch (JsonException jex)
            {
                Console.WriteLine($"⚠ JSON parse error: {jex.Message}");
                Console.WriteLine($"  Content-Type: {response.Content.Headers.ContentType?.MediaType ?? "<unknown>"}");
                Console.WriteLine($"  Snippet: {snippet}");
                await Task.Delay(RequestIntervalTimeOut);
                return 0;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при получении количества: {ex.Message}");
            return 0;
        }
    }
    private async Task DownloadFileAsync(string url, string outputPath)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            await using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            await response.Content.CopyToAsync(fs);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ Ошибка при скачивании файла {url}: {ex.Message}");
        }
    }
    public async Task<bool> SyncFavoritesToLiteDbAsync(string apiKey, string userId, string outputFolder, bool forceSync = false)
    {
        if (_cts.IsCancellationRequested)
            return false;
        Directory.CreateDirectory(outputFolder);

        using var db = new LiteDatabase(Path.Combine(outputFolder, Constants.LiteDBFilename));
        var postsCol = db.GetCollection<PostDocument>("posts");
        var metaCol = db.GetCollection<SyncMetadata>("metadata");

        var meta = metaCol.FindById("sync_metadata");
        int? localFavoritesCount = meta?.FavoritesCount;

        int currentCount = await GetFavoritePostCountAsync(apiKey, userId);
        
        var isFavoritesPostsCountChanged = localFavoritesCount.HasValue && currentCount != localFavoritesCount.Value;

        if(localFavoritesCount.HasValue)
        {
            Console.WriteLine($"Количество постов в БД: {localFavoritesCount}");
            Console.WriteLine($"Количество постов на Gelbooru: {currentCount}");
        }
        if (_cts.IsCancellationRequested)
            return false;
        if (!forceSync)
        {
            if (!isFavoritesPostsCountChanged)
            {
                Console.WriteLine("Избранное не изменилось, синхронизация новых постов не требуется.");
                return isFavoritesPostsCountChanged;
            }
            else
            {
                Console.WriteLine("Избранное изменилось, начата синхронизация");
            }
        } 
        else
        {
            Console.WriteLine("Принудительная синхронизация");
        }

        await Task.Delay(3000);
        var down = new GelbooruFavoriteDownloader(apiKey, userId);
        var currentPosts = await down.DownloadAllFavoritesAsync();

        //var currentPosts = await GetAllFavoritePostsAsync(apiKey, userId, currentCount);

        Console.WriteLine($"Начинаю синхронизацию {currentPosts.Count} постов...");

        SemaphoreSlim downloadSemaphore = new SemaphoreSlim(5); // для загрузки файлов
        object dbLock = new object(); // для защиты доступа к LiteDB

        var tasks = currentPosts.Select(async post =>
        {
            await downloadSemaphore.WaitAsync();
            try
            {
                var ext = Path.GetExtension(post.FileUrl);
                if (string.IsNullOrWhiteSpace(ext)) ext = ".bin";
                var fileName = $"{post.Id}{ext}";
                var filePath = Path.Combine(outputFolder, fileName);

                var newMeta = new PostDocument
                {
                    Id = post.Id,
                    Tags = post.Tags?.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(),
                    Rating = post.Rating,
                    FileUrl = post.FileUrl,
                    LocalPath = fileName,
                    Width = post.Width,
                    Height = post.Height,
                    Owner = post.Owner,
                    Source = post.Source,
                    CreatedAt = post.CreatedAt,
                    Md5 = post.Md5,
                    Status = post.Status,
                    HasComments = post.HasComments == "true",
                    HasNotes = post.HasNotes == "true",
                    Version = 1
                };

                PostDocument? oldMeta;
                lock (dbLock)
                {
                    oldMeta = postsCol.FindById(post.Id);
                }

                bool isNew = oldMeta == null;

                if (isNew)
                {
                    if (!File.Exists(filePath))
                    {
                        await DownloadFileAsync(post.FileUrl, filePath);
                    }
                }

                bool needUpsert = false;
                lock (dbLock)
                {
                    if (isNew)
                    {
                        postsCol.Upsert(newMeta);
                        needUpsert = true;
                    }
                    else
                    {
                        bool isUpdated = !PostDocumentEquals(oldMeta, newMeta);
                        if (isUpdated)
                        {
                            newMeta.Version = oldMeta.Version + 1;
                            postsCol.Upsert(newMeta);
                            needUpsert = true;
                        }
                    }
                    if (needUpsert)
                    {
                        db.Checkpoint(); // чтобы изменения сбросить на диск, опционально
                    }
                }

                if (isNew)
                    Console.WriteLine($"📥 Новый пост {post.Id}");
                else if (needUpsert)
                    Console.WriteLine($"♻️ Обновлён пост {post.Id}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обработке поста {post.Id}: {ex.Message}");
            }
            finally
            {
                downloadSemaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        
        var newMetaRecord = new SyncMetadata
        {
            Id = "sync_metadata",
            FavoritesCount = currentPosts.Count(),
            LastSyncedAt = DateTime.UtcNow
        };

        lock (dbLock)
        {
            metaCol.Upsert(newMetaRecord);
            db.Checkpoint();
        }

        Console.WriteLine("✅ Синхронизация избранных в базу данных завершена.");
        return isFavoritesPostsCountChanged;
    }
    private bool PostDocumentEquals(PostDocument a, PostDocument b)
    {
        // Сравниваем важные поля для обновления
        if (a.Tags == null && b.Tags != null) return false;
        if (a.Tags != null && b.Tags == null) return false;
        if (a.Tags != null && b.Tags != null)
        {
            if (!a.Tags.SequenceEqual(b.Tags)) return false;
        }

        return a.Rating == b.Rating &&
               a.FileUrl == b.FileUrl &&
               a.Width == b.Width &&
               a.Height == b.Height &&
               a.Owner == b.Owner &&
               a.Source == b.Source &&
               a.CreatedAt == b.CreatedAt &&
               a.Md5 == b.Md5 &&
               a.Status == b.Status &&
               a.HasComments == b.HasComments &&
               a.HasNotes == b.HasNotes;
    }
    public async Task<List<GelbooruTag>> DownloadTagTypesAsync(string apikey, string user_id, IEnumerable<string> allTags)
    {
        var result = new List<GelbooruTag>();
        var tagList = allTags
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        const int batchSize = 100;

        for (int i = 0; i < tagList.Count; i += batchSize)
        {
            var batch = tagList.Skip(i).Take(batchSize).ToList();
            string namesParam = Uri.EscapeDataString(string.Join(" ", batch));
            var url = GetTagsApiUrl(apikey, user_id, namesParam);

            try
            {
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var gelbooruTagResponse = JsonSerializer.Deserialize<GelbooruTagResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (gelbooruTagResponse?.Tags != null)
                {
                    result.AddRange(gelbooruTagResponse.Tags);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠ Ошибка при запросе тегов из Gelbooru API: {ex.Message}");
            }
        }

        return result;
    }
    public async Task DownloadTagsInfoToLiteDbAsync(string apikey, string user_id, string outputFolder)
    {
        if (_cts.IsCancellationRequested)
            return;
        using var db = new LiteDatabase(Path.Combine(outputFolder, Constants.LiteDBFilename));

        var postsCol = db.GetCollection<PostDocument>("posts");
        var tagsCol = db.GetCollection<GelbooruTag>("tags");

        var allTags = postsCol.FindAll()
            .SelectMany(p => p.Tags)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Console.WriteLine($"🔍 Обнаружено {allTags.Count} уникальных тегов.");

        const int tagBatchSize = 50;

        var tagBatches = allTags
            .Select((tag, index) => new { tag, index })
            .GroupBy(x => x.index / tagBatchSize)
            .Select(g => g.Select(x => x.tag).ToList())
            .ToList();

        int totalBatches = tagBatches.Count;
        int currentIndex = 0;

        while (currentIndex < totalBatches)
        {
            var batchStart = DateTime.UtcNow;
            var tasks = new List<Task<List<GelbooruTag>>>();

            for (int i = 0; i < MaxRequestsPerBatch && currentIndex < totalBatches; i++, currentIndex++)
            {
                var batch = tagBatches[currentIndex];
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var result = await DownloadTagTypesAsync(apikey, user_id, batch);
                        return result;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠ Ошибка при загрузке тегов: {string.Join(", ", batch.Take(3))}...: {ex.Message}");
                        return new List<GelbooruTag>();
                    }
                }));
            }

            var results = await Task.WhenAll(tasks);

            foreach (var tagList in results.Where(r => r.Count > 0))
            {
                tagsCol.Upsert(tagList);
            }

            Console.WriteLine($"✅ Обработано {Math.Min(currentIndex * tagBatchSize, allTags.Count)} / {allTags.Count} тегов.");

            var elapsed = (DateTime.UtcNow - batchStart).TotalMilliseconds;
            if (elapsed < RequestIntervalTimeOut && currentIndex < totalBatches)
            {
                await Task.Delay(RequestIntervalTimeOut - (int)elapsed);
            }
        }

        Console.WriteLine("🎉 Все теги загружены в базу.");
    }
}
