using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GelbooruBackup.Gelbooru.RequestHandlers
{
    public class FlareSolverrClient : IHttpClient
    {

#if DEBUG
        public static readonly string FlaresolverUrl = "http://localhost:8191";
#else
        public static readonly string FlaresolverUrl = "http://flaresolverr:8191";
#endif

        private readonly double _timeoutMs = 60000;
        private HttpClient _regularHttpClient;
        private HttpClient _httpClient;
        private string _sessionId;
        private string _userAgent;
        private bool _isInitialized;

        private readonly int _healthCheckRetries = 5;
        private readonly TimeSpan _healthCheckDelay = TimeSpan.FromSeconds(5);

        public string Name => "FlareSolverr";

        public async Task<bool> InitAsync(string username, string password)
        {
            if (_isInitialized) return true;

            try
            {
                _httpClient = new HttpClient();

                if (!await HealthCheckFlaresolverrAsync(_httpClient, _healthCheckRetries, _healthCheckDelay))
                    return false;

                _sessionId = await CreateSessionAsync();
                if (string.IsNullOrEmpty(_sessionId)) return false;

                _userAgent = await GetUserAgentAsync();
                if (string.IsNullOrEmpty(_userAgent)) return false;

                _regularHttpClient = new HttpClient();
                _regularHttpClient.Timeout = TimeSpan.FromMilliseconds(_timeoutMs);
                _regularHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgent);
                _regularHttpClient.DefaultRequestHeaders.Referrer = new Uri("https://gelbooru.com/");

                if (!await LoginAsync(username, password)) return false;

                _isInitialized = true;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<HttpResponseMessage> GetAsync(string url)
        {
            var response = await _regularHttpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
                return response;

            if (!await HealthCheckFlaresolverrAsync(_httpClient, _healthCheckRetries, _healthCheckDelay))
            {
                _isInitialized = false;
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            var request = new
            {
                cmd = "request.get",
                url = url,
                session = _sessionId,
                maxTimeout = _timeoutMs
            };

            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json"
            );

            var flareResponse = await _httpClient.PostAsync($"{FlaresolverUrl}/v1", content);
            var json = await flareResponse.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<FlareResponse>(json);

            if (result?.status != "ok" || result?.solution == null)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            if (result.solution.cookies != null)
            {
                foreach (var cookie in result.solution.cookies)
                {
                    _regularHttpClient.DefaultRequestHeaders.Add("Cookie", $"{cookie.name}={cookie.value}");
                }
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(result.solution.response, Encoding.UTF8, "text/html")
            };
        }

        private async Task<string> CreateSessionAsync()
        {
            var request = new { cmd = "sessions.create" };
            var result = await SendFlareRequestAsync<SessionResponse>(request);
            return result?.session;
        }

        private async Task<bool> LoginAsync(string username, string password)
        {
            var formData = new List<KeyValuePair<string, string>>
            {
                new("user", username),
                new("pass", password)
            };

            var request = new
            {
                cmd = "request.post",
                url = "https://gelbooru.com/index.php?page=account&s=login&code=00",
                session = _sessionId,
                postData = await new FormUrlEncodedContent(formData).ReadAsStringAsync(),
                maxTimeout = _timeoutMs
            };

            var result = await SendFlareRequestAsync<FlareResponse>(request);
            return result?.status == "ok";
        }

        private async Task<string> GetUserAgentAsync()
        {
            var response = await _httpClient.GetStringAsync(FlaresolverUrl);
            var info = JsonSerializer.Deserialize<FlareSolverrInfo>(response);
            return info?.userAgent ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";
        }

        private async Task<T?> SendFlareRequestAsync<T>(object request)
        {
            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync($"{FlaresolverUrl}/v1", content);
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(json);
        }

        public static async Task<bool> HealthCheckFlaresolverrAsync(HttpClient client, int retryCount, TimeSpan delay)
        {
            for (int i = 0; i < retryCount; i++)
            {
                try
                {
                    var response = await client.GetAsync(FlaresolverUrl);
                    if (response.IsSuccessStatusCode)
                        return true;
                }
                catch { }

                await Task.Delay(delay);
            }

            return false;
        }

        public void Dispose()
        {
            if (_httpClient != null && !string.IsNullOrEmpty(_sessionId))
            {
                try
                {
                    var request = new { cmd = "sessions.destroy", session = _sessionId };
                    var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
                    _httpClient.PostAsync($"{FlaresolverUrl}/v1", content).Wait();
                }
                catch { }
            }

            _httpClient?.Dispose();
            _regularHttpClient?.Dispose();
        }

        // Классы для десериализации
        private class SessionResponse { public string session { get; set; } }
        private class FlareSolverrInfo { public string userAgent { get; set; } }

        private class FlareResponse
        {
            public string status { get; set; }
            public string message { get; set; }
            public Solution solution { get; set; }
        }

        private class Solution
        {
            public string url { get; set; }
            public int status { get; set; }
            public string response { get; set; }
            public List<Cookie> cookies { get; set; }
        }

        private class Cookie
        {
            public string name { get; set; }
            public string value { get; set; }
            public string domain { get; set; }
            public string path { get; set; }
            public bool secure { get; set; }
            public bool httpOnly { get; set; }
        }
    }
}
