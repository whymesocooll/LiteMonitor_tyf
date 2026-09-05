using System.Net;
using System.Net.Http;
using System.Net.Security;

namespace LiteMonitor.src.Core
{
    /// <summary>
    /// 共享 HttpClient 提供者：统一"系统代理 + SSL 容错 + 自动解压 + UA"配置。
    /// 避免各模块各自临时 new HttpClient（每实例独占连接池，造成额外的 sockets 栈）。
    /// 共享实例的 Timeout 不可修改，需要超时控制的调用方请用 CancellationTokenSource.CancelAfter。
    /// </summary>
    public static class HttpProvider
    {
        /// <summary>进程级共享客户端（系统代理 + SSL 容错）。无全局超时，调用方自行用 CTS 控制。</summary>
        public static readonly HttpClient Default = Create();

        public static SocketsHttpHandler CreateHandler(Action<SocketsHttpHandler>? configure = null)
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                UseProxy = true,
                Proxy = WebRequest.GetSystemWebProxy(),
                // [Fix] 兼容用户证书/代理链异常的场景（与原各模块行为一致）
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = delegate { return true; }
                }
            };
            configure?.Invoke(handler);
            return handler;
        }

        public static HttpClient Create(Action<SocketsHttpHandler>? configure = null)
        {
            var client = new HttpClient(CreateHandler(configure));
            client.DefaultRequestHeaders.Add("User-Agent", "LiteMonitor/1.0");
            return client;
        }
    }
}
