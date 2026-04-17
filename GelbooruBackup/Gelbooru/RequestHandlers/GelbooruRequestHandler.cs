using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace GelbooruBackup.Gelbooru.RequestHandlers
{
    public class GelbooruRequestHandler : IDisposable
    {
        private readonly List<IHttpClient> _clients = new();
        private readonly string _username;
        private readonly string _password;

        public GelbooruRequestHandler(string username, string password)
        {
            _username = username;
            _password = password;
        }

        public GelbooruRequestHandler Use<T>() where T : IHttpClient, new()
        {
            _clients.Add(new T());
            return this;
        }

        public async Task<HttpResponseMessage> GetAsync(string url)
        {
            foreach (var client in _clients)
            {
                if (!await client.InitAsync(_username, _password))
                    continue;

                try
                {
                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                        return response;
                }
                catch { }
            }

            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        public void Dispose()
        {
            foreach (var client in _clients)
            {
                client.Dispose();
            }
        }
    }
}
