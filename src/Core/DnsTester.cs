using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LiteMonitor.src.Core
{
    /// <summary>
    /// 单台 DNS 服务器的测速结果
    /// </summary>
    public class DnsProbeResult
    {
        public string Address = "";
        public double AvgMs = -1;
        public double LossPct = 100;
        public int SuccessCount;
        public int TotalCount;
        public double TotalMs;
    }

    /// <summary>
    /// 最小 DNS 测速引擎：向指定 DNS 服务器发送 UDP 53 真实 A 查询，测量响应延迟与丢包。
    /// 移植自参考项目 DnsQuickSwitch/src/DnsProbe.cs，适配 .NET 8。
    /// </summary>
    public static class DnsTester
    {
        // 默认测速域名（国内常用站点，多域名并行取均值以平滑单站异常）
        public static readonly string[] DefaultDomains = { "www.baidu.com", "www.qq.com", "www.taobao.com" };

        private static int BuildQueryId() => Random.Shared.Next(0, 65536);

        public static async Task<DnsProbeResult> ProbeAsync(
            string serverIp,
            string[]? domains = null,
            int attemptsPerDomain = 3,
            int timeoutMs = 1500,
            CancellationToken ct = default)
        {
            domains ??= DefaultDomains;
            var result = new DnsProbeResult { Address = serverIp };

            if (!IPAddress.TryParse(serverIp, out var ip) || domains.Length == 0)
            {
                return result;
            }

            var lockObj = new object();
            var tasks = new List<Task>();

            foreach (var domain in domains)
            {
                tasks.Add(Task.Run(async () =>
                {
                    for (int i = 0; i < attemptsPerDomain; i++)
                    {
                        if (ct.IsCancellationRequested) return;

                        double ms;
                        bool ok;
                        try
                        {
                            var sw = Stopwatch.StartNew();
                            ok = await QueryOnceAsync(ip, domain, timeoutMs).ConfigureAwait(false);
                            sw.Stop();
                            ms = sw.Elapsed.TotalMilliseconds;
                        }
                        catch (Exception ex)
                        {
                            Log.Warn($"[DNS] 探测 {serverIp} 查询 {domain} 异常: {ex.Message}");
                            ok = false;
                            ms = timeoutMs;
                        }

                        lock (lockObj)
                        {
                            result.TotalCount++;
                            if (ok)
                            {
                                result.SuccessCount++;
                                result.TotalMs += ms;
                            }
                        }
                    }
                }, ct));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            if (result.SuccessCount > 0)
            {
                result.AvgMs = result.TotalMs / result.SuccessCount;
            }
            result.LossPct = result.TotalCount == 0
                ? 100
                : (1.0 - (double)result.SuccessCount / result.TotalCount) * 100.0;

            return result;
        }

        // 不给 Task.Delay 挂 CancellationToken：取消延迟最多等一个 timeoutMs，
        // 但可避免 TaskCanceledException 未观察导致 UnobservedTaskException 噪音
        private static async Task<bool> QueryOnceAsync(IPAddress ip, string domain, int timeoutMs)
        {
            byte[] query = BuildQuery(domain);

            using var udp = new UdpClient();
            udp.Connect(ip, 53);
            await udp.SendAsync(query, query.Length).ConfigureAwait(false);

            // 无参 ReceiveAsync 返回 Task<UdpReceiveResult>，可直接 WhenAny 竞速
            Task<UdpReceiveResult> receiveTask = udp.ReceiveAsync();
            Task timeoutTask = Task.Delay(timeoutMs);
            Task completed = await Task.WhenAny(receiveTask, timeoutTask).ConfigureAwait(false);

            if (completed == timeoutTask)
            {
                udp.Close();
                try { await receiveTask.ConfigureAwait(false); }
                catch { /* 超时后关闭 socket 导致的异常忽略 */ }
                return false;
            }

            UdpReceiveResult resp = await receiveTask.ConfigureAwait(false);
            return ValidateResponse(query, resp.Buffer);
        }

        // 手工构造 DNS 查询报文：Header(12) + QNAME + QTYPE=A + QCLASS=IN
        private static byte[] BuildQuery(string domain)
        {
            using var ms = new MemoryStream();
            int id = BuildQueryId();
            ms.WriteByte((byte)(id >> 8));
            ms.WriteByte((byte)(id & 0xFF));

            // Flags: RD=1
            ms.WriteByte(0x01);
            ms.WriteByte(0x00);

            // QDCOUNT=1
            ms.WriteByte(0x00);
            ms.WriteByte(0x01);

            // ANCOUNT / NSCOUNT / ARCOUNT = 0
            for (int i = 0; i < 6; i++) ms.WriteByte(0);

            foreach (var label in domain.Split('.'))
            {
                byte[] bytes = Encoding.ASCII.GetBytes(label);
                if (bytes.Length == 0 || bytes.Length > 63) continue;
                ms.WriteByte((byte)bytes.Length);
                ms.Write(bytes, 0, bytes.Length);
            }
            ms.WriteByte(0);

            // QTYPE=A(1), QCLASS=IN(1)
            ms.WriteByte(0x00);
            ms.WriteByte(0x01);
            ms.WriteByte(0x00);
            ms.WriteByte(0x01);

            return ms.ToArray();
        }

        // 只校验 Transaction ID 匹配 + QR 位为 1（是响应）
        private static bool ValidateResponse(byte[] query, byte[]? response)
        {
            if (response == null || response.Length < 12) return false;
            if (response[0] != query[0] || response[1] != query[1]) return false;
            if ((response[2] & 0x80) == 0) return false;
            return true;
        }
    }
}
