using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GelbooruBackup.Gelbooru.RequestHandlers
{
    public interface IHttpClient : IDisposable
    {
        Task<bool> InitAsync(string username, string password);
        Task<HttpResponseMessage> GetAsync(string url);
        string Name { get; }
    }
}
