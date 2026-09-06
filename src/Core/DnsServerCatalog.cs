using System.Collections.Generic;

namespace LiteMonitor.src.Core
{
    /// <summary>
    /// 预设 DNS 服务器（含运行时测速状态）
    /// </summary>
    public class DnsServerInfo
    {
        public string Name = "";
        // 稳定分类键：Domestic / International / IPv6，显示文案走语言 key Dns.Cat.*
        public string CategoryKey = "Domestic";
        public string Address = "";
        public string Address2 = "";

        // 运行时状态（不入配置）
        public double AvgMs = -1;
        public double LossPct = 100;
        public bool IsCurrent;
        public bool IsTesting;

        public bool IsIpv6 => Address.Contains(':');
    }

    public static class DnsServerCatalog
    {
        public static List<DnsServerInfo> GetDefault()
        {
            return new List<DnsServerInfo>
            {
                new DnsServerInfo { Name = "阿里 DNS",        CategoryKey = "Domestic",       Address = "223.5.5.5",      Address2 = "223.6.6.6" },
                new DnsServerInfo { Name = "114 DNS",         CategoryKey = "Domestic",       Address = "114.114.114.114", Address2 = "114.114.115.115" },
                new DnsServerInfo { Name = "腾讯 DNSPod",     CategoryKey = "Domestic",       Address = "119.29.29.29",   Address2 = "" },
                new DnsServerInfo { Name = "百度 DNS",        CategoryKey = "Domestic",       Address = "180.76.76.76",   Address2 = "" },
                new DnsServerInfo { Name = "360 DNS",         CategoryKey = "Domestic",       Address = "101.226.4.6",    Address2 = "218.30.118.6" },
                new DnsServerInfo { Name = "CNNIC DNS",       CategoryKey = "Domestic",       Address = "1.2.4.8",        Address2 = "210.2.4.8" },

                new DnsServerInfo { Name = "Google DNS",      CategoryKey = "International",  Address = "8.8.8.8",        Address2 = "8.8.4.4" },
                new DnsServerInfo { Name = "Cloudflare",      CategoryKey = "International",  Address = "1.1.1.1",        Address2 = "1.0.0.1" },
                new DnsServerInfo { Name = "Quad9",           CategoryKey = "International",  Address = "9.9.9.9",        Address2 = "" },
                new DnsServerInfo { Name = "OpenDNS",         CategoryKey = "International",  Address = "208.67.222.222", Address2 = "" },

                new DnsServerInfo { Name = "阿里 IPv6",       CategoryKey = "IPv6",           Address = "2400:3200::1",         Address2 = "" },
                new DnsServerInfo { Name = "114 IPv6",        CategoryKey = "IPv6",           Address = "240c::6666",           Address2 = "" },
                new DnsServerInfo { Name = "Google IPv6",     CategoryKey = "IPv6",           Address = "2001:4860:4860::8888", Address2 = "" },
                new DnsServerInfo { Name = "Cloudflare IPv6", CategoryKey = "IPv6",           Address = "2606:4700:4700::1111", Address2 = "" }
            };
        }
    }
}
