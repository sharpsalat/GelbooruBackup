using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GelbooruBackup.Gelbooru.RequestHandlers
{
    public class DefaultHttpClient : IHttpClient
    {
        private HttpClient _httpClient;
        public string Name => "Default";
        //private double _timeoutMilliseconds = 60000;
        private bool _isInitialized;

        public async Task<bool> InitAsync(string username, string password)
        {
            if (_isInitialized) return true;

            try
            {
                _httpClient = new HttpClient();
                //_httpClient.Timeout = TimeSpan.FromMilliseconds(_timeoutMilliseconds);
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                _httpClient.DefaultRequestHeaders.Referrer = new Uri("https://gelbooru.com/");

                var loginData = new List<KeyValuePair<string, string>>
                {
                    new("user", username),
                    new("pass", password)
                };

                var loginResponse = await _httpClient.PostAsync(
                "https://gelbooru.com/index.php?page=account&s=login&code=00",
                new FormUrlEncodedContent(loginData));

                _isInitialized = true;
                return loginResponse.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<HttpResponseMessage> GetAsync(string url)
        {
            return await _httpClient.GetAsync(url);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
