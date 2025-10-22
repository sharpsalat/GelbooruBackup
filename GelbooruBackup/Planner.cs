using GelbooruBackup.Gelbooru;
using GelbooruBackup.Szurubooru;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GelbooruBackup;
public class Planner : IDisposable
{
    private readonly Config _config;

    private CancellationTokenSource _cts;
    private Task _loopTask;

    public Planner(Config config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public void Start()
    {
        if (_cts != null)
            throw new InvalidOperationException("Planner already started.");

        _cts = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        if (_cts == null)
            return;

        _cts.Cancel();

        try
        {
            await _loopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // нормально прервано
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var lastForceSync = DateTime.MinValue;

        while (!cancellationToken.IsCancellationRequested)
        {

            var now = DateTime.UtcNow;

            bool needForce = (now - lastForceSync).TotalSeconds >= _config.FullSyncTimeout;
            Console.WriteLine(needForce ? "Полная синхронизация" : "Частичная синхронизация");
            try
            {
                if (needForce)
                {
                    await Sync(_config, true);
                    lastForceSync = DateTime.UtcNow;
                }
                else
                {
                    await Sync(_config, false);
                }
            }
            catch (Exception ex)
            {
                // TODO: логирование ошибки
                Console.Error.WriteLine($"Sync failed: {ex}");
            }

            // Рассчитаем время до следующего запуска
            now = DateTime.UtcNow;
            int delaySeconds;

            if (needForce)
            {
                delaySeconds = _config.ShortSyncTimeout;
            }
            else
            {
                // Если только обычный sync был, то ждём до следующего forceSync или регулярного запуска
                var nextForceIn = lastForceSync.AddSeconds(_config.FullSyncTimeout) - now;
                var nextRegularIn = TimeSpan.FromSeconds(_config.ShortSyncTimeout);
                delaySeconds = (int)Math.Min(nextForceIn.TotalSeconds, nextRegularIn.TotalSeconds);
                if (delaySeconds < 0)
                    delaySeconds = 0;
            }
            GC.Collect();            // Запускает сборку мусора всех поколений
            GC.WaitForPendingFinalizers(); // Ждёт завершения финализаторов
            GC.Collect();
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
        }
    }

    private async Task Sync(Config config, bool forceSync = false)
    {
        var isUpdated = await SyncFromGelbooru(config, forceSync);
        if (_cts.IsCancellationRequested)
            return;
        if (isUpdated || forceSync)
        {
            await SyncToSzurubooru(config);
        }
    }

    public async Task<bool> SyncFromGelbooru(Config config, bool forceSync = false)
    {
        var favouritesOwnerId = string.IsNullOrEmpty(config.FavouritesOwnerId) ? config.GelbooruUserId : config.FavouritesOwnerId;
        var gelbooruClient = new GelbooruClient(_cts, config.GelbooruUsername, config.GelbooruPassword);
        var hasNewPosts = await gelbooruClient.SyncFavoritesToLiteDbAsync(config.GelbooruApiKey, config.GelbooruUserId, favouritesOwnerId, config.FilesFolderPath, forceSync);
        if (_cts.IsCancellationRequested)
            return false;
        if (forceSync || hasNewPosts)
        {
            if (forceSync) Console.WriteLine("Принудительная синхронизация, начинаю синхронизацию тэгов в БД");
            else Console.WriteLine("Избранное изменилось, начинаю синхронизацию тэгов в БД");
            await gelbooruClient.DownloadTagsInfoToLiteDbAsync(config.GelbooruApiKey, config.GelbooruUserId, config.FilesFolderPath);
            return true;
        }
        else
        {
            Console.WriteLine("Избранное не изменилось, обычная синхронизация, пропускаю синхронизацию тэгов в БД");
        }
        return false;
    }
    public async Task SyncToSzurubooru(Config config)
    {
        var szuru = new SzurubooruAuthHelper(config.SzurubooruURL);
        var szurubooruClient = new SzurubooruClient(_cts);
        var isAdminUserCreated = await szuru.CreateFirstUserWithoutAuthAsync(config.SzurubooruUserName, config.SzurubooruUserPassword);
        if (!isAdminUserCreated)
        {
            Console.WriteLine("❌ Не удалось создать первого пользователя. Проверьте настройки и попробуйте снова.");
            throw new InvalidOperationException("Не удалось создать первого пользователя.");
        }
        var szurubooruUserToken = await szuru.GetOrCreateUserTokenAsync(config.SzurubooruUserName, config.SzurubooruUserPassword);
        await szurubooruClient.CreateDefaultTagCategoriesAsync(config.SzurubooruURL, config.SzurubooruUserName, szurubooruUserToken);
        await szurubooruClient.UploadTagsToSzurubooruWithUpdateAsync(config.SzurubooruURL, config.SzurubooruUserName, szurubooruUserToken, config.FilesFolderPath);
        await szurubooruClient.UploadPostsToSzuru(config.SzurubooruURL, config.SzurubooruUserName, szurubooruUserToken, config.FilesFolderPath);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}