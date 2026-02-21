using GelbooruBackup.Entities;
using LiteDB;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace GelbooruBackup.Szurubooru;
public class SzurubooruClient
{
    private CancellationTokenSource _cts;

    public SzurubooruClient(CancellationTokenSource cts)
    {
        _cts = cts;
    }

    public async Task CreateTagCategoryAsync(string szuruUrl, string username, string apiKey, string name, string color, int order)
    {
        var client = new HttpClient();
        string authString = $"{username}:{apiKey}";
        string base64AuthString = Convert.ToBase64String(Encoding.UTF8.GetBytes(authString));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", base64AuthString);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = new
        {
            name,
            color,
            order
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var url = szuruUrl.TrimEnd('/') + "/tag-categories";

        var response = await client.PostAsync(url, content);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            Console.WriteLine($"✔ Категория '{name}' создана.");
        }
        else
        {
            try
            {
                using var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;

                if (root.TryGetProperty("name", out var nameProp) &&
                    nameProp.GetString() == "TagCategoryAlreadyExistsError")
                {
                    Console.WriteLine($"ℹ Категория '{name}' уже существует.");
                    return;
                }
            }
            catch
            {
                // Игнорировать ошибки парсинга
            }

            Console.WriteLine($"⚠ Ошибка создания категории '{name}': {response.StatusCode}, {responseContent}");
        }
    }

    public async Task CreateDefaultTagCategoriesAsync(string szuruUrl, string username, string apiKey)
    {
        if (_cts.IsCancellationRequested)
            return;
        var categories = new[]
        {
        new { Name = "artist", Color = "#aa0000", Order = 1 },
        new { Name = "copyright", Color = "#aa00aa", Order = 2 },
        new { Name = "character", Color = "#00aa00", Order = 3 },
        new { Name = "meta", Color = "#ff8800", Order = 4 },
        new { Name = "general", Color = "#808080", Order = 5 }
    };

        foreach (var c in categories)
        {
            if (_cts.IsCancellationRequested)
                return;
            await CreateTagCategoryAsync(szuruUrl, username, apiKey, c.Name, c.Color, c.Order);
            await Task.Delay(50);
        }
    }
    public async Task UploadTagsToSzurubooruWithUpdateAsync(string szuruUrl, string username, string apiKey, string outputFolder)
    {
        if (_cts.IsCancellationRequested)
            return;
        using var db = new LiteDatabase(Path.Combine(outputFolder, Constants.LiteDBFilename));
        var tagCol = db.GetCollection<GelbooruTag>(GelbooruTag.TableName);
        Console.WriteLine($"🔍 Всего тегов в БД: {tagCol.Count()}");
        var syncedCol = db.GetCollection<SyncedToSzurubooruTag>(SyncedToSzurubooruTag.TableName);
        Console.WriteLine($"🔍 Всего синхронизированных тегов в БД: {syncedCol.Count()}");
        syncedCol.EnsureIndex(x => x.Name);

        // Загружаем имена уже синхронизированных тегов
        var alreadySynced = syncedCol.FindAll().Select(t => t.Name).ToHashSet();

        // Отбираем только несинхронизированные теги
        var unsyncedTags = tagCol.FindAll().Where(tag => !alreadySynced.Contains(tag.Name)).ToList();
        Console.WriteLine($"🔍 Всего несинхронизированных тегов: {unsyncedTags.Count}");

        var client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        string authString = $"{username}:{apiKey}";
        string base64AuthString = Convert.ToBase64String(Encoding.UTF8.GetBytes(authString));
        var authHeader = new AuthenticationHeaderValue("Token", base64AuthString);
        client.DefaultRequestHeaders.Authorization = authHeader;

        SemaphoreSlim semaphore = new SemaphoreSlim(15);
        object dbLock = new object();

        var tasks = unsyncedTags.Select(async tag =>
        {
            await semaphore.WaitAsync();

            var payload = new
            {
                names = new[] { tag.Name },
                category = tag.Type switch
                {
                    1 => "artist",
                    3 => "copyright",
                    4 => "character",
                    5 => "meta",
                    _ => "general"
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var url = szuruUrl.TrimEnd('/') + "/tags";

            try
            {
                await Task.Delay(50); // чтобы избежать флуда
                var response = await client.PostAsync(url, content);
                bool success = false;

                if (!response.IsSuccessStatusCode)
                {
                    var responseContentString = await response.Content.ReadAsStringAsync();
                    if ((int)response.StatusCode == 400 || IsTagAlreadyExistsError(responseContentString))
                    {                       
                        Console.WriteLine($"🔁 {tag.Name} уже существует, пробую обновить...");
                        var tagInfo = await FetchTagInfoAsync(client, szuruUrl, tag.Name);
                        // ⚠ Важно: "version" обязателен!
                        var updatePayload = new
                        {
                            version = tagInfo.version, // актуальную версию лучше вытаскивать с GET заранее
                            category = payload.category,
                            names = new[] { tag.Name }
                        };

                        var putJson = JsonSerializer.Serialize(updatePayload);
                        var putContent = new StringContent(putJson, Encoding.UTF8, "application/json");

                        var putUrl = szuruUrl.TrimEnd('/') + "/tag/" + Uri.EscapeDataString(tag.Name);

                        var putRequest = new HttpRequestMessage(HttpMethod.Put, putUrl)
                        {
                            Content = putContent
                        };

                        // Если нужен токен авторизации — добавь:
                        // putRequest.Headers.Authorization = new AuthenticationHeaderValue("Token", "твой_токен");

                        var putResponse = await client.SendAsync(putRequest);
                        responseContentString = await putResponse.Content.ReadAsStringAsync();

                        if (putResponse.IsSuccessStatusCode)
                        {
                            Console.WriteLine($"✅ Обновлено: {tag.Name}");
                            success = true;
                        }
                        else
                        {
                            Console.WriteLine($"⚠ Не удалось обновить {tag.Name}: {putResponse.StatusCode}, {responseContentString}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"⚠ Ошибка при добавлении {tag.Name}: {response.StatusCode}, {responseContentString}");
                    }
                }
                else
                {
                    Console.WriteLine($"✔ Добавлен тег: {tag.Name}");
                    success = true;
                }

                if (success)
                {
                    lock (dbLock)
                    {
                        if (!syncedCol.Exists(x => x.Name == tag.Name))
                        {
                            syncedCol.Insert(new SyncedToSzurubooruTag { Name = tag.Name });
                        }
                        db.Checkpoint();
                        if (_cts.IsCancellationRequested)
                            return;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠ Исключение при добавлении {tag.Name}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }

            await Task.Delay(50); // чтобы избежать флуда
        });

        await Task.WhenAll(tasks);
        Console.WriteLine("🎉 Все теги обработаны.");
    }
    public async Task UploadPostsToSzuru(string szurubooruApiUrl, string username, string apiKey, string outputFolder)
    {
        using var db = new LiteDatabase(Path.Combine(outputFolder, Constants.LiteDBFilename));
        var postsCol = db.GetCollection<PostDocument>(PostDocument.TableName);
        var syncedPostsCol = db.GetCollection<SyncedToSzurubooruPost>(SyncedToSzurubooruPost.TableName);

        var allPosts = postsCol.FindAll().ToList();
        var synced = syncedPostsCol.FindAll().ToList();

        var unsyncedPosts = allPosts.Where(p => !synced.Any(s => s.Id == p.Id)).ToList();
        var updatedPosts = allPosts.Where(p => synced.Any(s => s.Id == p.Id) && synced.First(s => s.Id == p.Id).Version != p.Version).ToList();

        using var client = new HttpClient();
        string authString = $"{username}:{apiKey}";
        string base64AuthString = Convert.ToBase64String(Encoding.UTF8.GetBytes(authString));
        var authHeader = new AuthenticationHeaderValue("Token", base64AuthString);
        client.DefaultRequestHeaders.Authorization = authHeader;
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        SemaphoreSlim semaphore = new SemaphoreSlim(10);
        object dbLock = new object(); // для защиты доступа к LiteDB
        // Новые посты
        var newTasks = unsyncedPosts.Select(async post =>
        {
            await semaphore.WaitAsync();
            try
            {
                string filePath = Path.Combine(outputFolder, post.LocalPath);
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"❌ Файл не найден: {filePath}");
                    return;
                }

                Console.WriteLine($"⬆ Загружаю файл {post.LocalPath}...");

                var tags = post.Tags?.Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList() ?? new();
                var safety = post.Rating?.ToLower() switch
                {
                    "safe" => "safe",
                    "questionable" => "sketchy",
                    "explicit" => "unsafe",
                    _ => "safe"
                };
                var source = $"https://gelbooru.com/index.php?page=post&s=view&id={post.Id}";

                var multipart = new MultipartFormDataContent
            {
                {
                    new StreamContent(File.OpenRead(filePath))
                    {
                        Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") }
                    },
                    "content",
                    Path.GetFileName(filePath)
                }
            };

                var query = new List<string>
            {
                $"safety={Uri.EscapeDataString(safety)}",
                $"source={Uri.EscapeDataString(source)}",
                $"tags={string.Join(",", tags.Select(Uri.EscapeDataString))}"
            };

                var uploadUrl = $"{szurubooruApiUrl.TrimEnd('/')}/posts?{string.Join("&", query)}";
                var uploadResp = await client.PostAsync(uploadUrl, multipart);
                var responseBody = await uploadResp.Content.ReadAsStringAsync();

                if (uploadResp.IsSuccessStatusCode)
                {
                    var created = JsonDocument.Parse(responseBody);
                    if (created.RootElement.TryGetProperty("id", out var szId))
                    {
                        long szurubooruId = szId.GetInt64();
                        lock (dbLock)
                        {
                            syncedPostsCol.Upsert(new SyncedToSzurubooruPost
                            {
                                Id = post.Id,
                                SzurubooruId = szurubooruId,
                                Version = post.Version,

                            });
                            db.Checkpoint();
                            if (_cts.IsCancellationRequested)
                                return;
                        }
                        Console.WriteLine($"✅ Пост загружен: {post.Id}");
                    }
                }
                else
                {
                    Console.WriteLine($"⚠ Ошибка при загрузке поста {post.Id}: {uploadResp.StatusCode} — {responseBody}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠ Ошибка при загрузке поста {post.Id}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        });
        await Task.WhenAll(newTasks);
        Console.WriteLine("🎉 Загрузка новых постов завершена.");
        // Обновление метаданных
        var updateTasks = updatedPosts.Select(async post =>
        {
            await semaphore.WaitAsync();
            try
            {
                var syncedModel = synced.First(sm => sm.Id == post.Id);

                Console.WriteLine($"🔄 Обновляю метаданные поста GelbooruId={post.Id}/SzurubooruId={syncedModel.SzurubooruId}...");

                var getUrl = $"{szurubooruApiUrl.TrimEnd('/')}/post/{syncedModel.SzurubooruId}";

                #region request
                var request = new HttpRequestMessage(HttpMethod.Get, getUrl);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = authHeader;
                var getResp = await client.SendAsync(request);
                #endregion

                var getBody = await getResp.Content.ReadAsStringAsync();

                if (!getResp.IsSuccessStatusCode)
                {
                    Console.WriteLine($"⚠ Не удалось получить пост {post.Id}: {getResp.StatusCode} — {getBody}");
                    return;
                }

                var jsonDoc = JsonDocument.Parse(getBody);
                if (!jsonDoc.RootElement.TryGetProperty("version", out var versionElement))
                {
                    Console.WriteLine($"⚠ Версия не найдена у поста {post.Id}");
                    return;
                }

                //var version = versionElement.GetInt32();
                var tags = post.Tags?.Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList() ?? new();
                var safety = post.Rating?.ToLower() switch
                {
                    "safe" => "safe",
                    "questionable" => "sketchy",
                    "explicit" => "unsafe",
                    _ => "safe"
                };
                var source = $"https://gelbooru.com/index.php?page=post&s=view&id={post.Id}";

                var version = versionElement;
                var updateData = new
                {
                    version,
                    source,
                    tags,
                    safety,
                };

                var content = new StringContent(JsonSerializer.Serialize(updateData), Encoding.UTF8, "application/json");
                var putUrl = $"{szurubooruApiUrl.TrimEnd('/')}/post/{syncedModel.SzurubooruId}";
                var putResp = await client.PutAsync(putUrl, content);
                var putBody = await putResp.Content.ReadAsStringAsync();

                if (putResp.IsSuccessStatusCode)
                {
                    syncedModel.Version = post.Version;
                    lock (dbLock)
                    {
                        syncedPostsCol.Update(syncedModel);
                        db.Checkpoint();
                    }
                    Console.WriteLine($"✅ Пост {post.Id} обновлён.");
                }
                else
                {
                    Console.WriteLine($"⚠ Ошибка при обновлении поста {post.Id}: {putResp.StatusCode} — {putBody}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠ Ошибка при обновлении поста {post.Id}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        });


        await Task.WhenAll(updateTasks);
        Console.WriteLine("🎉 Обновление метаданных постов завершено.");

        Console.WriteLine("🎉 Синхронизация постов завершена.");
    }
    public async Task<SzurubooruTagInfo?> FetchTagInfoAsync(HttpClient client, string szuruUrl, string tagName)
    {
        var url = szuruUrl.TrimEnd('/') + "/tag/" + Uri.EscapeDataString(tagName);

        var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"⚠ Не удалось получить тег {tagName}: {response.StatusCode}");
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        try
        {
            
            var tagInfo = JsonSerializer.Deserialize<SzurubooruTagInfo>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return tagInfo;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Ошибка при десериализации информации о теге {tagName}: {ex.Message}");
            Console.WriteLine($"Ответ JSON:\n{json}");
            return null;
        }
    }
    public static bool IsTagAlreadyExistsError(string responseContentString)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseContentString);
            if (doc.RootElement.TryGetProperty("name", out JsonElement nameElement))
            {
                return nameElement.GetString() == "TagAlreadyExistsError";
            }
        }
        catch (JsonException)
        {
            // Некорректный JSON
        }

        return false;
    }

}
