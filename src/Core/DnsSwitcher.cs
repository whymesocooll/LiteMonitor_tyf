using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace LiteMonitor.src.Core
{
    /// <summary>
    /// 可操作的网卡信息。Guid 用于查注册表，Alias 用于 netsh。
    /// </summary>
    public class DnsAdapterInfo
    {
        public string Guid = "";
        public string Alias = "";
        public string Description = "";
        public bool HasGateway;

        public override string ToString() => Alias;
    }

    /// <summary>
    /// 网卡当前 DNS（区分静态/DHCP 来源）
    /// </summary>
    public class DnsCurrentInfo
    {
        public string Servers = "";
        public bool IsStatic;
    }

    public class DnsSwitchResult
    {
        public bool Success;
        public string Message = "";
    }

    /// <summary>
    /// DNS 切换器：netsh 设置 DNS + 注册表读取当前 DNS。
    /// 程序 manifest 已要求管理员权限，netsh 可直接调用。
    /// 移植自参考项目 DnsQuickSwitch/src/DnsSwitcher.cs，命令执行改用 ArgumentList 参数化。
    /// </summary>
    public static class DnsSwitcher
    {
        public static List<DnsAdapterInfo> GetAdapters()
        {
            var result = new List<DnsAdapterInfo>();

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                var ipProps = ni.GetIPProperties();
                result.Add(new DnsAdapterInfo
                {
                    Guid = ni.Id,
                    Alias = ni.Name,
                    Description = ni.Description,
                    HasGateway = ipProps.GatewayAddresses.Count > 0
                });
            }

            // 优先有默认网关的网卡；若都没有，则返回所有 Up 的非虚拟网卡
            var withGateway = result.Where(a => a.HasGateway).ToList();
            return withGateway.Count > 0 ? withGateway : result;
        }

        public static DnsCurrentInfo GetCurrentDns(string adapterGuid, bool ipv6)
        {
            var info = new DnsCurrentInfo();
            string baseKey = ipv6
                ? @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces"
                : @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(baseKey + "\\" + adapterGuid);
                if (key == null) return info;

                // NameServer 存在即为用户手动设置的静态 DNS；否则回退 DhcpNameServer
                if (key.GetValue("NameServer") is string ns && !string.IsNullOrWhiteSpace(ns))
                {
                    info.Servers = ns.Trim();
                    info.IsStatic = true;
                    return info;
                }

                if (key.GetValue("DhcpNameServer") is string dhcp && !string.IsNullOrWhiteSpace(dhcp))
                {
                    info.Servers = dhcp.Trim();
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[DNS] 读取当前 DNS 失败 ({adapterGuid}): {ex.Message}");
            }

            return info;
        }

        /// <summary>
        /// 把网卡 DNS 切换为静态主/备地址。ipv6=true 时操作 IPv6 栈。
        /// </summary>
        public static DnsSwitchResult SwitchTo(string adapterAlias, string primary, string secondary, bool ipv6)
        {
            var result = new DnsSwitchResult();
            try
            {
                string? warning = null;
                string ctx = ipv6 ? "ipv6" : "ip";

                RunNetSh(ctx, "set", "dns", $"name={adapterAlias}", "static", primary, "validate=no");
                if (!string.IsNullOrEmpty(secondary))
                {
                    try
                    {
                        RunNetSh(ctx, "add", "dns", $"name={adapterAlias}", secondary, "index=2", "validate=no");
                    }
                    catch (Exception ex)
                    {
                        warning = "备用 DNS 添加失败: " + ex.Message;
                    }
                }

                RunIpConfigFlushDns();

                result.Success = true;
                result.Message = warning ?? "切换成功";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 恢复网卡的 DNS 为自动获取（DHCP），参考项目缺失此能力，作为撤销切换的安全出口。
        /// 同时恢复 IPv4 与 IPv6，单个协议栈失败不影响另一个。
        /// </summary>
        public static DnsSwitchResult RestoreDhcp(string adapterAlias)
        {
            var result = new DnsSwitchResult();
            var errors = new List<string>();

            foreach (var ctx in new[] { "ip", "ipv6" })
            {
                try
                {
                    RunNetSh(ctx, "set", "dns", $"name={adapterAlias}", "source=dhcp");
                }
                catch (Exception ex)
                {
                    errors.Add($"{(ctx == "ip" ? "IPv4" : "IPv6")}: {ex.Message}");
                }
            }

            try { RunIpConfigFlushDns(); } catch { /* 刷新失败不阻断 */ }

            if (errors.Count == 0)
            {
                result.Success = true;
                result.Message = "已恢复自动获取(DHCP)";
            }
            else if (errors.Count < 2)
            {
                // 部分成功：无 IPv6 环境时 ipv6 栈可能报错，视为成功
                result.Success = true;
                result.Message = "IPv4 已恢复自动获取";
            }
            else
            {
                result.Success = false;
                result.Message = string.Join("; ", errors);
            }

            return result;
        }

        // netsh 调用统一走 ArgumentList 参数化（照抄 SystemActions 的安全模式），
        // 含空格的网卡名由 ArgumentList 自动加引号
        private static void RunNetSh(string context, string verb, string subject, params string[] args)
        {
            var psi = new ProcessStartInfo("netsh")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            psi.ArgumentList.Add("interface");
            psi.ArgumentList.Add(context);
            psi.ArgumentList.Add(verb);
            psi.ArgumentList.Add(subject);
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi)!;
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();

            if (p.ExitCode != 0)
            {
                string detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                throw new Exception(string.IsNullOrWhiteSpace(detail)
                    ? $"netsh 命令失败（退出码 {p.ExitCode}）"
                    : "netsh 失败: " + detail.Trim());
            }
        }

        private static void RunIpConfigFlushDns()
        {
            var psi = new ProcessStartInfo("ipconfig")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("/flushdns");

            using var p = Process.Start(psi)!;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
        }
    }
}
