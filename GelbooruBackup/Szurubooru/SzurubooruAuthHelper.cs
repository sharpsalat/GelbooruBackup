using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GelbooruBackup.Szurubooru;
public class SzurubooruAuthHelper
{
    private readonly string _szuruUrl;

    public SzurubooruAuthHelper(string szuruUrl)
    {
        _szuruUrl = szuruUrl.TrimEnd('/');
    }
    public async Task<bool> CreateFirstUserWithoutAuthAsync(string username, string password, string? email = null)
    {
        using var client = new HttpClient();
        var url = $"{_szuruUrl}/users";

        var payload = new Dictionary<string, object>
    {
        { "name", username },
        { "password", password }
    };

        if (!string.IsNullOrEmpty(email))
            payload["email"] = email;

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.PostAsync(url, content);
        var responseJson = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            Console.WriteLine($"🎉 Первый пользователь '{username}' создан и автоматически стал админом.");
            return true;
        }
        else
        {
            try
            {
                using var doc = JsonDocument.Parse(responseJson);
                if (doc.RootElement.TryGetProperty("name", out var nameProp) &&
                    nameProp.GetString() == "UserAlreadyExistsError")
                {
                    Console.WriteLine($"ℹ Пользователь '{username}' уже существует.");
                    return true; // считаем, что цель достигнута — пользователь есть
                }
            }
            catch
            {
                // Игнорируем ошибки парсинга JSON
            }

            Console.WriteLine($"⛔ Ошибка при создании первого пользователя: {response.StatusCode}\n{responseJson}");
            return false;
        }
    }
    public async Task<string?> GetOrCreateUserTokenAsync(string username, string password)
    {
        using var client = new HttpClient();

        // Формируем Basic Auth заголовок
        string basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

        // Обязательно принимаем JSON
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Путь к API токенам пользователя
        string getTokensUrl = $"{_szuruUrl}/user-tokens/{Uri.EscapeDataString(username)}";

        var tokensResponse = await client.GetAsync(getTokensUrl);
        if (tokensResponse.IsSuccessStatusCode)
        {
            var tokensJson = await tokensResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(tokensJson);

            // Проверяем массив токенов (ключ "tokens" или "userTokens" — уточни по API)
            if (doc.RootElement.TryGetProperty("results", out var resultsArray))
            {
                foreach (var tokenEntry in resultsArray.EnumerateArray())
                {
                    if (tokenEntry.TryGetProperty("enabled", out var enabledProp) && enabledProp.GetBoolean())
                    {
                        if (tokenEntry.TryGetProperty("token", out var tokenProp))
                        {
                            return tokenProp.GetString();
                        }
                    }
                }
            }
        }
        else if ((int)tokensResponse.StatusCode != 404)
        {
            var error = await tokensResponse.Content.ReadAsStringAsync();
            Console.WriteLine($"Ошибка получения токенов: {tokensResponse.StatusCode}, {error}");
            return null;
        }

        // Токенов нет или 404 — создаём новый
        string createTokenUrl = $"{_szuruUrl}/user-token/{Uri.EscapeDataString(username)}";

        var createPayload = new
        {
            enabled = true,
            note = "Auto-generated token"
        };

        var createJson = JsonSerializer.Serialize(createPayload);
        var content = new StringContent(createJson, Encoding.UTF8, "application/json");

        var createResponse = await client.PostAsync(createTokenUrl, content);
        if (createResponse.IsSuccessStatusCode)
        {
            var responseJson = await createResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("token", out var tokenProp))
            {
                return tokenProp.GetString();
            }
        }
        else
        {
            var error = await createResponse.Content.ReadAsStringAsync();
            Console.WriteLine($"Ошибка создания токена: {createResponse.StatusCode}, {error}");
        }

        return null;
    }
}
