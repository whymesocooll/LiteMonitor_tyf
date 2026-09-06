using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LiteMonitor.src.Core;

namespace LiteMonitor
{
    /// <summary>
    /// DNS 测速与一键切换工具窗口（替换原"历史流量统计"菜单入口）。
    /// 交互流程：打开即自动并行测速全部预设 → 按延迟排序 → 点"切换"成功后窗口自动关闭。
    /// </summary>
    public class DnsToolForm : Form
    {
        private readonly Theme _currentTheme;
        private readonly Settings _cfg;

        private Label lblTitle = null!;
        private Label lblAdapter = null!;
        private ComboBox cmbAdapter = null!;
        private Button btnRefreshAdapter = null!;
        private Label lblCurrent = null!;
        private Label lblStatus = null!;
        private Panel listPanel = null!;
        private Button btnRetest = null!;
        private Button btnDhcp = null!;
        private Button btnClose = null!;

        // 每行 UI 与数据映射
        private readonly Dictionary<DnsServerInfo, Panel> _rows = new();
        private readonly Dictionary<DnsServerInfo, Label> _rowLatency = new();
        private readonly Dictionary<DnsServerInfo, Label> _rowLoss = new();
        private readonly Dictionary<DnsServerInfo, Label> _rowBadge = new();
        private readonly Dictionary<DnsServerInfo, Button> _rowSwitch = new();
        private readonly List<DnsServerInfo> _servers = new();
        private readonly List<DnsAdapterInfo> _adapters = new();

        // 共享字体（避免每行重复创建）
        private Font _fName = null!;
        private Font _fAddr = null!;
        private Font _fStat = null!;
        private Font _fBadge = null!;

        private CancellationTokenSource? _testCts;
        private bool _testing;
        private bool _switching;
        private bool _loadingAdapters;
        private bool _isDisposed;

        private Point _dragOffset;

        public DnsToolForm()
        {
            _currentTheme = ThemeManager.Current;
            _cfg = Settings.Load();

            FormBorderStyle = FormBorderStyle.None;
            Width = ScaleDPI(640);
            int desiredH = ScaleDPI(800);
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Height = Math.Min(desiredH, wa.Height - ScaleDPI(40));
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = ThemeManager.ParseColor(_currentTheme.Color.Background);
            ForeColor = ThemeManager.ParseColor(_currentTheme.Color.TextPrimary);
            TopMost = true;
            ShowInTaskbar = false;
            Opacity = _cfg.Opacity;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.DoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

            _fName = new Font(_currentTheme.Font.Family, 9f, FontStyle.Bold);
            _fAddr = new Font(_currentTheme.Font.Family, 7.5f, FontStyle.Regular);
            _fStat = new Font(_currentTheme.Font.Family, 9.5f, FontStyle.Bold);
            _fBadge = new Font(_currentTheme.Font.Family, 8.5f, FontStyle.Bold);

            BuildUi();
            BuildRows();

            MakeMovable(this);
            MakeMovable(lblTitle);
            MakeMovable(lblAdapter);
            MakeMovable(lblCurrent);
            MakeMovable(lblStatus);

            ApplyRounded();
        }

        private void BuildUi()
        {
            int pad = ScaleDPI(16);

            lblTitle = new Label
            {
                Text = "📡 " + T("Dns.Title"),
                AutoSize = false,
                Width = Width,
                Height = ScaleDPI(30),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(_currentTheme.Font.Family, 12, FontStyle.Bold),
                ForeColor = ThemeManager.ParseColor(_currentTheme.Color.TextTitle),
                Top = ScaleDPI(12)
            };

            lblAdapter = new Label
            {
                Text = T("Dns.Adapter"),
                AutoSize = false,
                Width = ScaleDPI(72),
                Height = ScaleDPI(26),
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font(_currentTheme.Font.Family, 9),
                ForeColor = ThemeManager.ParseColor(_currentTheme.Color.TextGroup),
                Top = ScaleDPI(50),
                Left = pad
            };

            int refreshW = ScaleDPI(56);
            cmbAdapter = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font(_currentTheme.Font.Family, 9),
                Top = ScaleDPI(50),
                Left = pad + ScaleDPI(80),
                Width = Width - pad * 2 - ScaleDPI(80) - refreshW - ScaleDPI(8),
                Height = ScaleDPI(26),
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemeManager.ParseColor(_currentTheme.Color.GroupBackground),
                ForeColor = ThemeManager.ParseColor(_currentTheme.Color.TextPrimary)
            };
            cmbAdapter.SelectedIndexChanged += (_, __) =>
            {
                if (_loadingAdapters) return;
                var adapter = SelectedAdapter;
                if (adapter == null) return;
                if (_cfg.DnsAdapterAlias != adapter.Alias)
                {
                    _cfg.DnsAdapterAlias = adapter.Alias;
                    _cfg.Save();
                }
                _ = RefreshCurrentDnsAsync();
            };

            btnRefreshAdapter = new Button
            {
                Text = T("Dns.Refresh"),
                Width = refreshW,
                Height = ScaleDPI(26),
                Top = ScaleDPI(50),
                Left = Width - pad - refreshW,
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemeManager.ParseColor(_currentTheme.Color.GroupBackground),
                ForeColor = ThemeManager.ParseColor(_currentTheme.Color.TextPrimary),
                Font = new Font(_currentTheme.Font.Family, 8.5f)
            };
            btnRefreshAdapter.FlatAppearance.BorderSize = 0;
            btnRefreshAdapter.Click += (_, __) => _ = LoadAdaptersAsync();

            lblCurrent = new Label
            {
                Text = "…",
                AutoSize = false,
                Top = ScaleDPI(86),
                Left = pad,
                Width = Width - pad * 2,
                Height = ScaleDPI(42),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(_currentTheme.Font.Family, 8.5f),
                ForeColor = ThemeManager.ParseColor(_currentTheme.Color.TextGroup),
                BackColor = ThemeManager.ParseColor(_currentTheme.Color.GroupBackground)
            };

            lblStatus = new Label
            {
                Text = T("Dns.NotTested"),
                AutoSize = false,
                Top = ScaleDPI(134),
                Left = pad,
                Width = Width - pad * 2,
                Height = ScaleDPI(18),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(_currentTheme.Font.Family, 8.5f),
                ForeColor = ThemeManager.ParseColor(_currentTheme.Color.TextGroup)
            };

            listPanel = new Panel
            {
                Top = ScaleDPI(158),
                Left = ScaleDPI(12),
                Width = Width - ScaleDPI(24),
                Height = Height - ScaleDPI(158) - ScaleDPI(58),
                AutoScroll = true,
                BackColor = ThemeManager.ParseColor(_currentTheme.Color.Background)
            };

            int btnY = Height - ScaleDPI(44);
            int btnH = ScaleDPI(30);
            btnRetest = MakeButton(T("Dns.TestAll"), pad, btnY, ScaleDPI(96), btnH);
            btnRetest.Click += (_, __) => StartTestAll();

            btnDhcp = MakeButton(T("Dns.RestoreDhcp"), pad + ScaleDPI(106), btnY, ScaleDPI(150), btnH);
            btnDhcp.Click += (_, __) => _ = RestoreDhcpAsync();

            btnClose = MakeButton(T("Dns.Close"), Width - pad - ScaleDPI(80), btnY, ScaleDPI(80), btnH);
            btnClose.Click += (_, __) => Close();

            Controls.AddRange(new Control[] { lblTitle, lblAdapter, cmbAdapter, btnRefreshAdapter, lblCurrent, lblStatus, listPanel, btnRetest, btnDhcp, btnClose });
        }

        private Button MakeButton(string text, int left, int top, int width, int height)
        {
            var b = new Button
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = height,
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemeManager.ParseColor(_currentTheme.Color.GroupBackground),
                ForeColor = ThemeManager.ParseColor(_currentTheme.Color.TextPrimary),
                Font = new Font(_currentTheme.Font.Family, 9)
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private void BuildRows()
        {
            _servers.Clear();
            _servers.AddRange(DnsServerCatalog.GetDefault());

            int rowGap = ScaleDPI(4);
            int rowH = ScaleDPI(44);
            int y = ScaleDPI(2);
            int rowW = listPanel.Width - ScaleDPI(4) - SystemInformation.VerticalScrollBarWidth;

            foreach (var s in _servers)
            {
                var row = new Panel
                {
                    Left = ScaleDPI(2),
                    Top = y,
                    Width = rowW,
                    Height = rowH,
                    BackColor = ThemeManager.ParseColor(_currentTheme.Color.GroupBackground)
                };
                y += rowH + rowGap;

                // 右侧列固定锚定：按钮 → 当前徽标 → 丢包 → 延迟，剩余给名称区
                int btnW = ScaleDPI(64);
                int btnX = rowW - btnW - ScaleDPI(10);
                int badgeX = btnX - ScaleDPI(54);
                int lossX = badgeX - ScaleDPI(92);
                int latX = lossX - ScaleDPI(96);
                int nameW = latX - ScaleDPI(22);

                var lblName = new Label
                {
                    Text = s.Name + "  ·  " + T("Dns.Cat." + s.CategoryKey),
                    AutoSize = false,
                    Left = ScaleDPI(10),
                    Top = ScaleDPI(5),
                    Width = nameW,
                    Height = ScaleDPI(18),
                    Font = _fName,
                    ForeColor = ThemeManager.ParseColor(_currentTheme.Color.TextPrimary)
                };

                var lblAddr = new Label
                {
                    Text = s.IsIpv6 ? s.Address : s.Address + (string.IsNullOrEmpty(s.Address2) ? "" : " / " + s.Address2),
                    AutoSize = false,
                    Left = ScaleDPI(10),
                    Top = ScaleDPI(24),
                    Width = nameW,
                    Height = ScaleDPI(14),
                    Font = _fAddr,
                    ForeColor = ThemeManager.ParseColor(_currentTheme.Color.TextGroup)
                };

                var lblLat = new Label
                {
                    Text = "—",
                    AutoSize = false,
                    Left = latX,
                    Top = ScaleDPI(6),
                    Width = ScaleDPI(90),
                    Height = ScaleDPI(18),
                    TextAlign = ContentAlignment.MiddleRight,
                    Font = _fStat,
                    ForeColor = ThemeManager.ParseColor(_currentTheme.Color.TextGroup)
                };

                var lblLoss = new Label
                {
                    Text = string.Format(T("Dns.Loss"), 100),
                    AutoSize = false,
                    Left = lossX,
                    Top = ScaleDPI(25),
                    Width = ScaleDPI(86),
                    Height = ScaleDPI(14),
                    TextAlign = ContentAlignment.MiddleRight,
                    Font = _fAddr,
                    ForeColor = ThemeManager.ParseColor(_currentTheme.Color.TextGroup)
                };

                var lblBadge = new Label
                {
                    Text = "",
                    AutoSize = false,
                    Left = badgeX,
                    Top = ScaleDPI(13),
                    Width = ScaleDPI(48),
                    Height = ScaleDPI(18),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = _fBadge,
                    ForeColor = ThemeManager.ParseColor(_currentTheme.Color.ValueSafe)
                };

                var btn = MakeButton(T("Dns.Switch"), btnX, ScaleDPI(8), btnW, rowH - ScaleDPI(16));
                btn.Font = new Font(_currentTheme.Font.Family, 8.5f);
                btn.Click += async (_, __) => await SwitchToServerAsync(s);

                row.Controls.AddRange(new Control[] { lblName, lblAddr, lblLat, lblLoss, lblBadge, btn });
                listPanel.Controls.Add(row);

                _rows[s] = row;
                _rowLatency[s] = lblLat;
                _rowLoss[s] = lblLoss;
                _rowBadge[s] = lblBadge;
                _rowSwitch[s] = btn;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (Owner != null) CenterToParent();
            else
            {
                Rectangle screen = Screen.FromPoint(Cursor.Position).WorkingArea;
                Location = new Point(
                    screen.Left + (screen.Width - Width) / 2,
                    screen.Top + (screen.Height - Height) / 2
                );
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 打开即自动：加载网卡 → 回读当前 DNS → 并行测速全部预设
            Task.Run(RunInitAsync);
        }

        private async Task RunInitAsync()
        {
            try
            {
                await LoadAdaptersAsync();
                await RefreshCurrentDnsAsync();
                StartTestAll();
            }
            catch (Exception ex)
            {
                Log.Error("[DNS] 窗口初始化失败", ex);
            }
        }

        private DnsAdapterInfo? SelectedAdapter =>
            _adapters.Count == 0 ? null : (cmbAdapter.SelectedItem as DnsAdapterInfo) ?? _adapters[0];

        private async Task LoadAdaptersAsync()
        {
            _loadingAdapters = true;
            try
            {
                var list = await Task.Run(() => DnsSwitcher.GetAdapters());
                SafeInvoke(() =>
                {
                    _adapters.Clear();
                    _adapters.AddRange(list);
                    cmbAdapter.Items.Clear();
                    foreach (var a in _adapters) cmbAdapter.Items.Add(a);

                    if (_adapters.Count == 0)
                    {
                        cmbAdapter.Items.Add(T("Dns.NoAdapter"));
                        cmbAdapter.SelectedIndex = 0;
                        lblCurrent.Text = T("Dns.NoAdapter");
                        return;
                    }

                    // 记忆的网卡优先，其次第一个有网关的（GetAdapters 已按网关优先排序）
                    int idx = _adapters.FindIndex(a => a.Alias == _cfg.DnsAdapterAlias);
                    cmbAdapter.SelectedIndex = idx >= 0 ? idx : 0;
                });
            }
            catch (Exception ex)
            {
                Log.Error("[DNS] 加载网卡列表失败", ex);
            }
            finally
            {
                _loadingAdapters = false;
            }
        }

        private async Task RefreshCurrentDnsAsync()
        {
            var adapter = SelectedAdapter;
            if (adapter == null) return;

            string guid = adapter.Guid;
            var (v4, v6) = await Task.Run(() =>
                (DnsSwitcher.GetCurrentDns(guid, false), DnsSwitcher.GetCurrentDns(guid, true)));

            SafeInvoke(() =>
            {
                if (_isDisposed || IsDisposed) return;
                lblCurrent.Text = $"IPv4: {FormatCurrent(v4)}\nIPv6: {FormatCurrent(v6)}";
                ApplyCurrentBadges(v4, v6);
            });
        }

        private static string FormatCurrent(DnsCurrentInfo info)
        {
            if (string.IsNullOrWhiteSpace(info.Servers)) return T("Dns.None");
            return info.Servers + " (" + T(info.IsStatic ? "Dns.Static" : "Dns.Dhcp") + ")";
        }

        // 把注册表读到的当前 DNS 与预设比对，标记"当前"徽标并禁用对应行的切换按钮
        private void ApplyCurrentBadges(DnsCurrentInfo v4, DnsCurrentInfo v6)
        {
            var v4Set = SplitDns(v4.Servers);
            var v6Set = SplitDns(v6.Servers);

            foreach (var s in _servers)
            {
                var set = s.IsIpv6 ? v6Set : v4Set;
                s.IsCurrent = set.Contains(s.Address);
                _rowBadge[s].Text = s.IsCurrent ? "✔ " + T("Dns.Current") : "";
                _rowSwitch[s].Enabled = !s.IsCurrent;
            }
        }

        private static HashSet<string> SplitDns(string servers)
        {
            return new HashSet<string>(
                (servers ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);
        }

        // ===========================================
        // 自动测速：并行测全部预设，完成后按延迟升序重排
        // ===========================================
        private void StartTestAll()
        {
            if (_testing) return;
            _testing = true;
            _testCts?.Cancel();
            _testCts = new CancellationTokenSource();
            Task.Run(() => RunTestAllAsync(_testCts.Token));
        }

        private async Task RunTestAllAsync(CancellationToken ct)
        {
            int total = _servers.Count;
            int done = 0;
            SafeInvoke(() =>
            {
                btnRetest.Enabled = false;
                lblStatus.Text = string.Format(T("Dns.TestingProgress"), 0, total);
            });

            var tasks = _servers.Select(async s =>
            {
                s.IsTesting = true;
                SafeInvoke(() => UpdateRow(s));
                try
                {
                    var r = await DnsTester.ProbeAsync(s.Address, DnsTester.DefaultDomains, 3, 1500, ct);
                    s.AvgMs = r.AvgMs;
                    s.LossPct = r.LossPct;
                }
                catch (Exception ex)
                {
                    Log.Warn($"[DNS] 测速任务失败 ({s.Address}): {ex.Message}");
                }
                s.IsTesting = false;
                int d = Interlocked.Increment(ref done);
                SafeInvoke(() =>
                {
                    UpdateRow(s);
                    lblStatus.Text = string.Format(T("Dns.TestingProgress"), Math.Min(d, total), total);
                });
            });

            await Task.WhenAll(tasks);

            SafeInvoke(() =>
            {
                // 多台不同运营商的 DNS 都亚毫秒应答在物理上不可能——大概率是本地代理(TUN)劫持了 UDP 53，
                // 本地直接应答导致所有服务器都显示 0ms，此时结果仅供当前网络环境参考
                int hijacked = _servers.Count(s => s.AvgMs >= 0 && s.AvgMs < 1.5);
                lblStatus.Text = hijacked >= 3 ? T("Dns.ProxyHint") : T("Dns.TestDone");
                btnRetest.Enabled = true;
                SortRowsByLatency();
            });
            _testing = false;
        }

        private void UpdateRow(DnsServerInfo s)
        {
            if (_isDisposed || IsDisposed) return;
            var lat = _rowLatency[s];
            var loss = _rowLoss[s];

            if (s.IsTesting)
            {
                lat.Text = T("Dns.Testing");
                lat.ForeColor = ThemeManager.ParseColor(_currentTheme.Color.TextGroup);
                return;
            }

            if (s.AvgMs < 0)
            {
                lat.Text = T("Dns.Timeout");
                loss.Text = string.Format(T("Dns.Loss"), 100);
                lat.ForeColor = ThemeManager.ParseColor(_currentTheme.Color.ValueCrit);
                loss.ForeColor = ThemeManager.ParseColor(_currentTheme.Color.ValueCrit);
                return;
            }

            lat.Text = $"{s.AvgMs:F0} ms";
            loss.Text = string.Format(T("Dns.Loss"), s.LossPct);

            // 延迟配色：<100 绿，<250 黄，其余红；丢包 ≥50% 直接红
            Color c = s.AvgMs < 100 ? ThemeManager.ParseColor(_currentTheme.Color.ValueSafe)
                    : s.AvgMs < 250 ? ThemeManager.ParseColor(_currentTheme.Color.ValueWarn)
                    : ThemeManager.ParseColor(_currentTheme.Color.ValueCrit);
            if (s.LossPct >= 50) c = ThemeManager.ParseColor(_currentTheme.Color.ValueCrit);
            lat.ForeColor = c;
            loss.ForeColor = s.LossPct >= 50 ? c : ThemeManager.ParseColor(_currentTheme.Color.TextGroup);
        }

        private void SortRowsByLatency()
        {
            var ordered = _servers
                .OrderBy(s => s.AvgMs < 0 ? double.MaxValue : s.AvgMs)
                .ThenBy(s => s.LossPct)
                .ToList();

            // 行为绝对定位，按延迟重排只需重设 Top
            listPanel.SuspendLayout();
            int gap = ScaleDPI(4);
            int y = ScaleDPI(2);
            foreach (var s in ordered)
            {
                var row = _rows[s];
                row.Top = y;
                y += row.Height + gap;
            }
            listPanel.ResumeLayout();
        }

        // ===========================================
        // 一键切换：确认 → netsh → 成功自动关窗
        // ===========================================
        private async Task SwitchToServerAsync(DnsServerInfo s)
        {
            if (_switching) return;
            var adapter = SelectedAdapter;
            if (adapter == null) return;

            string family = s.IsIpv6 ? "IPv6" : "IPv4";
            string addrText = s.Address + (string.IsNullOrEmpty(s.Address2) ? "" : ", " + s.Address2);
            string confirmText = string.Format(T("Dns.ConfirmSwitch"), adapter.Alias, family, s.Name, addrText);
            if (MessageBox.Show(this, confirmText, T("Dns.Title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _switching = true;
            SetSwitchButtons(false);
            try
            {
                var curV4 = DnsSwitcher.GetCurrentDns(adapter.Guid, false);
                var curV6 = DnsSwitcher.GetCurrentDns(adapter.Guid, true);
                Log.Info($"[DNS] 切换前 {adapter.Alias} IPv4: {curV4.Servers}(static={curV4.IsStatic}) IPv6: {curV6.Servers}(static={curV6.IsStatic})");

                lblStatus.Text = string.Format(T("Dns.Switching"), s.Name);
                var result = await Task.Run(() => DnsSwitcher.SwitchTo(adapter.Alias, s.Address, s.Address2, s.IsIpv6));

                if (result.Success)
                {
                    Log.Info($"[DNS] 切换成功 {adapter.Alias} → {s.Name} ({addrText}){(string.IsNullOrEmpty(result.Message) ? "" : " | " + result.Message)}");
                    Log.Info("[DNS] 窗口自动关闭");
                    SafeInvoke(Close);
                }
                else
                {
                    Log.Warn($"[DNS] 切换失败 {adapter.Alias} → {s.Name}: {result.Message}");
                    lblStatus.Text = T("Dns.SwitchFailed");
                    MessageBox.Show(this, T("Dns.SwitchFailed") + ": " + result.Message, T("Dns.Title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                _switching = false;
                SafeInvoke(() => SetSwitchButtons(true));
            }
        }

        private async Task RestoreDhcpAsync()
        {
            if (_switching) return;
            var adapter = SelectedAdapter;
            if (adapter == null) return;

            string confirmText = string.Format(T("Dns.ConfirmDhcp"), adapter.Alias);
            if (MessageBox.Show(this, confirmText, T("Dns.Title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _switching = true;
            SetSwitchButtons(false);
            try
            {
                var curV4 = DnsSwitcher.GetCurrentDns(adapter.Guid, false);
                Log.Info($"[DNS] 恢复DHCP前 {adapter.Alias} IPv4: {curV4.Servers}(static={curV4.IsStatic})");

                var result = await Task.Run(() => DnsSwitcher.RestoreDhcp(adapter.Alias));
                if (result.Success)
                {
                    Log.Info($"[DNS] 已恢复DHCP {adapter.Alias}: {result.Message}");
                    lblStatus.Text = T("Dns.DhcpDone");
                    await RefreshCurrentDnsAsync();
                }
                else
                {
                    Log.Warn($"[DNS] 恢复DHCP失败 {adapter.Alias}: {result.Message}");
                    lblStatus.Text = T("Dns.DhcpFailed");
                    MessageBox.Show(this, T("Dns.DhcpFailed") + ": " + result.Message, T("Dns.Title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                _switching = false;
                SafeInvoke(() => SetSwitchButtons(true));
            }
        }

        private void SetSwitchButtons(bool enabled)
        {
            if (_isDisposed || IsDisposed) return;
            foreach (var s in _servers)
            {
                var btn = _rowSwitch[s];
                btn.Enabled = enabled && !s.IsCurrent;
            }
            btnDhcp.Enabled = enabled;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            _testCts?.Cancel();
            _testCts?.Dispose();
            _testCts = null;
            _isDisposed = true;
        }

        // ===========================================
        // 基础设施（与 SpeedTestForm 同款）
        // ===========================================
        private static string T(string key) => LanguageManager.T(key);

        private void SafeInvoke(Action action)
        {
            if (_isDisposed || IsDisposed || !IsHandleCreated) return;
            try { Invoke(action); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void MakeMovable(Control control)
        {
            control.MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left) _dragOffset = e.Location;
            };
            control.MouseMove += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    if (Math.Abs(e.X - _dragOffset.X) + Math.Abs(e.Y - _dragOffset.Y) < 1) return;
                    Location = new Point(Left + e.X - _dragOffset.X, Top + e.Y - _dragOffset.Y);
                }
            };
        }

        private int ScaleDPI(int value)
        {
            using Graphics g = CreateGraphics();
            return (int)(value * g.DpiX / 96f);
        }

        private void ApplyRounded()
        {
            try
            {
                var gp = new System.Drawing.Drawing2D.GraphicsPath();
                int cornerRadius = Math.Max(ScaleDPI(4), _currentTheme.Layout.CornerRadius);
                int diameter = cornerRadius * 2;
                gp.AddArc(0, 0, diameter, diameter, 180, 90);
                gp.AddArc(Width - diameter, 0, diameter, diameter, 270, 90);
                gp.AddArc(Width - diameter, Height - diameter, diameter, diameter, 0, 90);
                gp.AddArc(0, Height - diameter, diameter, diameter, 90, 90);
                gp.CloseFigure();
                Region = new Region(gp);
            }
            catch
            {
                // 圆角失败则退回默认圆角
                var gp = new System.Drawing.Drawing2D.GraphicsPath();
                int d = ScaleDPI(20);
                gp.AddArc(0, 0, d, d, 180, 90);
                gp.AddArc(Width - d, 0, d, d, 270, 90);
                gp.AddArc(Width - d, Height - d, d, d, 0, 90);
                gp.AddArc(0, Height - d, d, d, 90, 90);
                gp.CloseFigure();
                Region = new Region(gp);
            }
        }
    }
}
