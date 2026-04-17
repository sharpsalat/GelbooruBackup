//using FlareSolverrSharp;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Net;
//using System.Text;
//using System.Text.Json;
//using System.Threading.Tasks;

//namespace GelbooruBackup.Gelbooru
//{
//    public class GelbooruHttpClient : IDisposable
//    {
//        public HttpClient HttpClient { get; private set; }
//        public HttpClient FlareHttpClient { get; private set; }
//        private readonly string _sessionId;
//        private readonly string _flaresolverrUrl = "http://localhost:8191";
//        private readonly double _timeoutMilliseconds = 600000;

//        public GelbooruHttpClient(string username, string password, string flareresolverURL)
//        {
//            _flaresolverrUrl = flareresolverURL;

//            // 1. Создаем сессию в FlareSolverr
//            _sessionId = CreateSessionAsync().GetAwaiter().GetResult();
//            Console.WriteLine($"Сессия создана: {_sessionId}");

//            // 2. Получаем User-Agent
//            var userAgent = GetUserAgentAsync().GetAwaiter().GetResult();

//            // 3. Логинимся через сессию
//            LoginAsync(username, password, userAgent).GetAwaiter().GetResult();

//            // 4. Обычный клиент (для быстрых запросов)
//            HttpClient = new HttpClient();
//            HttpClient.Timeout = TimeSpan.FromMilliseconds(_timeoutMilliseconds);
//            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
//            HttpClient.DefaultRequestHeaders.Referrer = new Uri("https://gelbooru.com/");
//        }

//        private async Task<string> CreateSessionAsync()
//        {
//            using var client = new HttpClient();
//            var request = new
//            {
//                cmd = "sessions.create"
//            };

//            var content = new StringContent(
//                JsonSerializer.Serialize(request),
//                Encoding.UTF8,
//                "application/json"
//            );

//            var response = await client.PostAsync($"{_flaresolverrUrl}/v1", content);
//            var json = await response.Content.ReadAsStringAsync();
//            var result = JsonSerializer.Deserialize<SessionResponse>(json);

//            return result?.session ?? throw new Exception("Не удалось создать сессию");
//        }

//        private async Task LoginAsync(string username, string password, string userAgent)
//        {
//            using var client = new HttpClient();

//            var formData = new List<KeyValuePair<string, string>>
//            {
//                new("user", username),
//                new("pass", password)
//            };

//            var request = new
//            {
//                cmd = "request.post",
//                url = "https://gelbooru.com/index.php?page=account&s=login&code=00",
//                session = _sessionId,
//                postData = new FormUrlEncodedContent(formData).ReadAsStringAsync().Result,
//                maxTimeout = _timeoutMilliseconds
//            };

//            var content = new StringContent(
//                JsonSerializer.Serialize(request),
//                Encoding.UTF8,
//                "application/json"
//            );

//            var response = await client.PostAsync($"{_flaresolverrUrl}/v1", content);
//            var json = await response.Content.ReadAsStringAsync();
//            var result = JsonSerializer.Deserialize<FlareResponse>(json);

//            if (result?.status != "ok")
//                throw new Exception("Ошибка логина через FlareSolverr");
//        }

//        public async Task<HttpResponseMessage> GetAsync(string url)
//        {
//            // Пробуем обычным клиентом
//            var response = await HttpClient.GetAsync(url);
//            if (response.IsSuccessStatusCode)
//                return response;

//            // Если не сработало - идем через FlareSolverr с сессией
//            using var client = new HttpClient();

//            var request = new
//            {
//                cmd = "request.get",
//                url = url,
//                session = _sessionId,  // используем ту же сессию!
//                maxTimeout = _timeoutMilliseconds
//            };

//            var content = new StringContent(
//                JsonSerializer.Serialize(request),
//                Encoding.UTF8,
//                "application/json"
//            );

//            var flareResponse = await client.PostAsync($"{_flaresolverrUrl}/v1", content);
//            var json = await flareResponse.Content.ReadAsStringAsync();
//            var result = JsonSerializer.Deserialize<FlareResponse>(json);

//            if (result?.status != "ok" || result?.solution == null)
//                return new HttpResponseMessage(HttpStatusCode.Forbidden);

//            // Сохраняем куки из ответа в обычный клиент
//            if (result.solution.cookies != null)
//            {
//                foreach (var cookie in result.solution.cookies)
//                {
//                    HttpClient.DefaultRequestHeaders.Add("Cookie", $"{cookie.name}={cookie.value}");
//                }
//            }

//            // Возвращаем успешный ответ
//            return new HttpResponseMessage(HttpStatusCode.OK)
//            {
//                Content = new StringContent(result.solution.response, Encoding.UTF8, "text/html")
//            };
//        }

//        private async Task<string> GetUserAgentAsync()
//        {
//            using var client = new HttpClient();
//            client.Timeout = TimeSpan.FromSeconds(10);

//            var response = await client.GetStringAsync(_flaresolverrUrl);
//            var info = JsonSerializer.Deserialize<FlareSolverrInfo>(response);
//            return info?.userAgent ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";
//        }

//        public void Dispose()
//        {
//            // Удаляем сессию при завершении
//            try
//            {
//                using var client = new HttpClient();
//                var request = new
//                {
//                    cmd = "sessions.destroy",
//                    session = _sessionId
//                };
//                var content = new StringContent(
//                    JsonSerializer.Serialize(request),
//                    Encoding.UTF8,
//                    "application/json"
//                );
//                client.PostAsync($"{_flaresolverrUrl}/v1", content).Wait();
//            }
//            catch { }

//            HttpClient?.Dispose();
//        }

//        // Классы для десериализации
//        private class SessionResponse { public string session { get; set; } }
//        private class FlareSolverrInfo { public string userAgent { get; set; } }

//        private class FlareResponse
//        {
//            public string status { get; set; }
//            public string message { get; set; }
//            public Solution solution { get; set; }
//        }

//        private class Solution
//        {
//            public string url { get; set; }
//            public int status { get; set; }
//            public string response { get; set; }
//            public List<Cookie> cookies { get; set; }
//        }

//        private class Cookie
//        {
//            public string name { get; set; }
//            public string value { get; set; }
//            public string domain { get; set; }
//            public string path { get; set; }
//            public bool secure { get; set; }
//            public bool httpOnly { get; set; }
//        }
//    }
//}
