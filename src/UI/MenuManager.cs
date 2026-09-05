using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using LiteMonitor.src.SystemServices;
using LiteMonitor.src.Core;
using LiteMonitor.src.Core.Actions;
using LiteMonitor.src.UI;
using LiteMonitor.src.UI.Helpers;
using System.Collections.Generic;
using System.Diagnostics;
using LiteMonitor.src.SystemServices.InfoService;

namespace LiteMonitor
{
    public static class MenuManager
    {
        /// <summary>
        /// 一次 Build 的共享上下文，避免每个分段方法都传 (form, cfg, ui, targetPage) 四个参数
        /// </summary>
        private sealed class MenuContext
        {
            public readonly MainForm Form;
            public readonly Settings Cfg;
            public readonly UIController? Ui;
            public readonly string? TargetPage;
            public readonly bool IsTaskbarMode;

            public MenuContext(MainForm form, Settings cfg, UIController? ui, string? targetPage)
            {
                Form = form;
                Cfg = cfg;
                Ui = ui;
                TargetPage = targetPage;
                IsTaskbarMode = targetPage == "Taskbar";
            }
        }

        /// <summary>
        /// 构建 LiteMonitor 主菜单（右键菜单 + 托盘菜单）
        /// </summary>
        public static ContextMenuStrip Build(MainForm form, Settings cfg, UIController? ui, string targetPage = null)
        {
            var menu = new ContextMenuStrip();
            var ctx = new MenuContext(form, cfg, ui, targetPage);

            // 1. 清理内存
            AddCleanMemoryItem(ctx, menu);

            // 2. 显示模式 (置顶/任务栏/穿透/透明度/宽度/缩放等)
            menu.Items.Add(BuildDisplayModeRoot(ctx));

            // 3. 显示监控项 (委托给 MenuMonitorHelper 生成)
            menu.Items.Add(MenuMonitorHelper.Build(form, cfg, ui, ctx.IsTaskbarMode));

            // 4. 主题与工具窗口
            AddThemeAndToolsItems(ctx, menu);

            // 5. 设置中心入口
            AddSettingsItem(ctx, menu);

            // 6. 语言切换
            menu.Items.Add(BuildLanguageRoot(ctx));
            menu.Items.Add(new ToolStripSeparator());

            // 7. 开机启动
            menu.Items.Add(BuildAutoStartItem(ctx));

            // 8. 更多工具
            menu.Items.Add(BuildMoreRoot(ctx));
            menu.Items.Add(new ToolStripSeparator());

            // 9. 退出
            menu.Items.Add(BuildExitItem(ctx));

            return menu;
        }

        // ==================================================================================
        // 分段 1：清理内存
        // ==================================================================================
        private static void AddCleanMemoryItem(MenuContext ctx, ContextMenuStrip menu)
        {
            var cleanMem = new ToolStripMenuItem(LanguageManager.T("Menu.CleanMemory"));
            cleanMem.Image = Properties.Resources.CleanMem;
            cleanMem.Click += (_, __) => ctx.Form.CleanMemory();
            menu.Items.Add(cleanMem);
            menu.Items.Add(new ToolStripSeparator());
        }

        // ==================================================================================
        // 分段 2：显示模式 (置顶、显示模式、任务栏开关、隐藏主界面/托盘)
        // ==================================================================================
        private static ToolStripMenuItem BuildDisplayModeRoot(MenuContext ctx)
        {
            var cfg = ctx.Cfg;
            var form = ctx.Form;
            var ui = ctx.Ui;

            var modeRoot = new ToolStripMenuItem(LanguageManager.T("Menu.DisplayMode"));

            // === 置顶 ===
            var topMost = new ToolStripMenuItem(LanguageManager.T("Menu.TopMost"))
            {
                Checked = cfg.TopMost,
                CheckOnClick = true
            };
            topMost.CheckedChanged += (_, __) =>
            {
                cfg.TopMost = topMost.Checked;
                cfg.Save();
                // ★ 统一调用
                AppActions.ApplyWindowAttributes(cfg, form);
            };

            // === 垂直 / 水平 ===
            var vertical = new ToolStripMenuItem(LanguageManager.T("Menu.Vertical"))
            {
                Checked = !cfg.HorizontalMode
            };
            var horizontal = new ToolStripMenuItem(LanguageManager.T("Menu.Horizontal"))
            {
                Checked = cfg.HorizontalMode
            };

            // 辅助点击事件
            void SetMode(bool isHorizontal)
            {
                cfg.HorizontalMode = isHorizontal;
                cfg.Save();
                // ★ 统一调用 (含主题、布局刷新)
                AppActions.ApplyThemeAndLayout(cfg, ui, form);
            }

            vertical.Click += (_, __) => SetMode(false);
            horizontal.Click += (_, __) => SetMode(true);

            modeRoot.DropDownItems.Add(vertical);
            modeRoot.DropDownItems.Add(horizontal);
            modeRoot.DropDownItems.Add(new ToolStripSeparator());

            // === 任务栏显示 ===
            var taskbarMode = new ToolStripMenuItem(LanguageManager.T("Menu.TaskbarShow"))
            {
                Checked = cfg.ShowTaskbar
            };

            taskbarMode.Click += (_, __) =>
            {
                cfg.ShowTaskbar = !cfg.ShowTaskbar;
                // 保存
                cfg.Save();
                // ★ 统一调用 (含防呆检查、显隐逻辑、菜单刷新)
                AppActions.ApplyVisibility(cfg, form);
            };

            modeRoot.DropDownItems.Add(taskbarMode);

            // === 网页显示选项 (二级菜单) ===
            AddWebServerSubmenu(ctx, modeRoot);

            modeRoot.DropDownItems.Add(new ToolStripSeparator());

            // === 自动隐藏 ===
            var autoHide = new ToolStripMenuItem(LanguageManager.T("Menu.AutoHide"))
            {
                Checked = cfg.AutoHide,
                CheckOnClick = true
            };
            autoHide.CheckedChanged += (_, __) =>
            {
                cfg.AutoHide = autoHide.Checked;
                cfg.Save();
                // ★ 统一调用
                AppActions.ApplyWindowAttributes(cfg, form);
            };

            // Move TopMost here
            modeRoot.DropDownItems.Add(topMost);
            modeRoot.DropDownItems.Add(autoHide);

            // === 限制窗口拖出屏幕 (纯数据开关) ===
            var clampItem = new ToolStripMenuItem(LanguageManager.T("Menu.ClampToScreen"))
            {
                Checked = cfg.ClampToScreen,
                CheckOnClick = true
            };
            clampItem.CheckedChanged += (_, __) =>
            {
                cfg.ClampToScreen = clampItem.Checked;
                cfg.Save();
            };
            modeRoot.DropDownItems.Add(clampItem);

            // === 鼠标穿透 ===
            var clickThrough = new ToolStripMenuItem(LanguageManager.T("Menu.ClickThrough"))
            {
                Checked = cfg.ClickThrough,
                CheckOnClick = true
            };
            clickThrough.CheckedChanged += (_, __) =>
            {
                cfg.ClickThrough = clickThrough.Checked;
                cfg.Save();
                // ★ 统一调用
                AppActions.ApplyWindowAttributes(cfg, form);
            };
            modeRoot.DropDownItems.Add(clickThrough);

            modeRoot.DropDownItems.Add(new ToolStripSeparator());

            // === 透明度 / 界面宽度 / 界面缩放 ===
            modeRoot.DropDownItems.Add(BuildOpacitySubmenu(ctx));
            modeRoot.DropDownItems.Add(BuildWidthSubmenu(ctx));
            modeRoot.DropDownItems.Add(BuildScaleSubmenu(ctx));
            modeRoot.DropDownItems.Add(new ToolStripSeparator());

            // === 隐藏主窗口 ===
            var hideMainForm = new ToolStripMenuItem(LanguageManager.T("Menu.HideMainForm"))
            {
                Checked = cfg.HideMainForm,
                CheckOnClick = true
            };

            hideMainForm.CheckedChanged += (_, __) =>
            {
                cfg.HideMainForm = hideMainForm.Checked;
                cfg.Save();
                // ★ 统一调用
                AppActions.ApplyVisibility(cfg, form);
            };
            modeRoot.DropDownItems.Add(hideMainForm);

            // === 隐藏托盘图标 ===
            var hideTrayIcon = new ToolStripMenuItem(LanguageManager.T("Menu.HideTrayIcon"))
            {
                Checked = cfg.HideTrayIcon,
                CheckOnClick = true
            };

            hideTrayIcon.CheckedChanged += (_, __) =>
            {
                // 注意：旧的 CheckIfAllowHide 逻辑已整合进 AppActions.ApplyVisibility 的防呆检查中
                // 这里只需修改配置并调用 Action 即可
                cfg.HideTrayIcon = hideTrayIcon.Checked;
                cfg.Save();
                // ★ 统一调用
                AppActions.ApplyVisibility(cfg, form);
            };
            modeRoot.DropDownItems.Add(hideTrayIcon);

            return modeRoot;
        }

        /// <summary>
        /// 网页显示二级菜单 (启用开关 + 打开网页)
        /// </summary>
        private static void AddWebServerSubmenu(MenuContext ctx, ToolStripMenuItem modeRoot)
        {
            var cfg = ctx.Cfg;

            var itemWeb = new ToolStripMenuItem(LanguageManager.T("Menu.WebServer"));

            // 1. 子项：启用/禁用
            var itemWebEnable = new ToolStripMenuItem(LanguageManager.T("Menu.Enable"))
            {
                Checked = cfg.WebServerEnabled,
                CheckOnClick = true
            };

            // 2. 子项：打开网页 (动态获取 IP)
            var itemWebOpen = new ToolStripMenuItem(LanguageManager.T("Menu.OpenWeb"));
            itemWebOpen.Enabled = cfg.WebServerEnabled; // 只有开启时才可用

            // 事件：切换开关
            itemWebEnable.CheckedChanged += (s, e) =>
            {
                // 1. 更新配置
                cfg.WebServerEnabled = itemWebEnable.Checked;
                cfg.Save();

                // 2. ★ 立即应用（调用 AppActions 重启服务）
                AppActions.ApplyWebServer(cfg);

                // 3. 刷新“打开网页”按钮的可用状态
                itemWebOpen.Enabled = cfg.WebServerEnabled;

                // 4. [新增] 开启时弹窗引导
                if (cfg.WebServerEnabled)
                {
                    string msg = LanguageManager.T("Menu.WebServerTip");
                    if (MessageBox.Show(msg, "LiteMonitor", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK)
                    {
                        itemWebOpen.PerformClick();
                    }
                }
            };

            // 事件：打开网页
            itemWebOpen.Click += (s, e) =>
            {
                WebActions.OpenWebMonitor(cfg);
            };

            itemWeb.ToolTipText = LanguageManager.T("Menu.WebServerTip");
            itemWeb.DropDownItems.Add(itemWebEnable);
            itemWeb.DropDownItems.Add(itemWebOpen);
            modeRoot.DropDownItems.Add(itemWeb);
        }

        private static ToolStripMenuItem BuildOpacitySubmenu(MenuContext ctx)
        {
            var cfg = ctx.Cfg;
            var form = ctx.Form;
            var opacityRoot = new ToolStripMenuItem(LanguageManager.T("Menu.Opacity"));
            double[] presetOps = { 1.0, 0.95, 0.9, 0.85, 0.8, 0.75, 0.7, 0.6, 0.5, 0.4, 0.3 };

            // [Optimization] Shared handler to avoid closure per item
            EventHandler onOpacityClick = (s, e) =>
            {
                if (s is ToolStripMenuItem item && item.Tag is double val)
                {
                    cfg.Opacity = val;
                    cfg.Save();
                    AppActions.ApplyWindowAttributes(cfg, form);
                }
            };

            foreach (var val in presetOps)
            {
                var item = new ToolStripMenuItem($"{val * 100:0}%")
                {
                    Checked = Math.Abs(cfg.Opacity - val) < 0.01,
                    Tag = val
                };
                item.Click += onOpacityClick;
                opacityRoot.DropDownItems.Add(item);
            }
            return opacityRoot;
        }

        private static ToolStripMenuItem BuildWidthSubmenu(MenuContext ctx)
        {
            var cfg = ctx.Cfg;
            var ui = ctx.Ui;
            var form = ctx.Form;

            var widthRoot = new ToolStripMenuItem(LanguageManager.T("Menu.Width"));
            int[] presetWidths = { 180, 200, 220, 240, 260, 280, 300, 360, 420, 480, 540, 600, 660, 720, 780, 840, 900, 960, 1020, 1080, 1140, 1200 };
            int currentW = cfg.PanelWidth;

            // [Optimization] Shared handler
            EventHandler onWidthClick = (s, e) =>
            {
                if (s is ToolStripMenuItem item && item.Tag is int w)
                {
                    cfg.PanelWidth = w;
                    cfg.Save();
                    AppActions.ApplyThemeAndLayout(cfg, ui, form, retainData: true);
                }
            };

            foreach (var w in presetWidths)
            {
                var item = new ToolStripMenuItem($"{w}px")
                {
                    Checked = Math.Abs(currentW - w) < 1,
                    Tag = w
                };
                item.Click += onWidthClick;
                widthRoot.DropDownItems.Add(item);
            }
            return widthRoot;
        }

        private static ToolStripMenuItem BuildScaleSubmenu(MenuContext ctx)
        {
            var cfg = ctx.Cfg;
            var ui = ctx.Ui;
            var form = ctx.Form;

            var scaleRoot = new ToolStripMenuItem(LanguageManager.T("Menu.Scale"));
            (double val, string key)[] presetScales =
            {
                (2.00, "200%"), (1.75, "175%"), (1.50, "150%"), (1.25, "125%"),
                (1.00, "100%"), (0.90, "90%"),  (0.85, "85%"),  (0.80, "80%"),
                (0.75, "75%"),  (0.70, "70%"),  (0.60, "60%"),  (0.50, "50%")
            };

            double currentScale = cfg.UIScale;

            // [Optimization] Shared handler
            EventHandler onScaleClick = (s, e) =>
            {
                if (s is ToolStripMenuItem item && item.Tag is double scale)
                {
                    cfg.UIScale = scale;
                    cfg.Save();
                    AppActions.ApplyThemeAndLayout(cfg, ui, form, retainData: true);
                }
            };

            foreach (var (scale, label) in presetScales)
            {
                var item = new ToolStripMenuItem(label)
                {
                    Checked = Math.Abs(currentScale - scale) < 0.01,
                    Tag = scale
                };
                item.Click += onScaleClick;
                scaleRoot.DropDownItems.Add(item);
            }

            return scaleRoot;
        }

        // ==================================================================================
        // 分段 4：主题与工具窗口 (硬件详情 / 测速 / 监控历史 / 流量)
        // ==================================================================================
        private static void AddThemeAndToolsItems(MenuContext ctx, ContextMenuStrip menu)
        {
            var cfg = ctx.Cfg;

            // === 主题 ===
            var themeRoot = new ToolStripMenuItem(LanguageManager.T("Menu.Theme"));
            // 主题编辑器 (独立窗口，保持原样)
            var themeEditor = new ToolStripMenuItem(LanguageManager.T("Menu.ThemeEditor"));
            themeEditor.Image = Properties.Resources.ThemeIcon;
            themeEditor.Click += (_, __) => new ThemeEditor.ThemeEditorForm().Show();
            themeRoot.DropDownItems.Add(themeEditor);
            themeRoot.DropDownItems.Add(new ToolStripSeparator());

            foreach (var name in ThemeManager.GetAvailableThemes())
            {
                var item = new ToolStripMenuItem(name)
                {
                    Checked = name.Equals(cfg.Skin, StringComparison.OrdinalIgnoreCase)
                };

                item.Click += (_, __) =>
                {
                    cfg.Skin = name;
                    cfg.Save();
                    // ★ 统一调用
                    AppActions.ApplyThemeAndLayout(cfg, ctx.Ui, ctx.Form);
                };
                themeRoot.DropDownItems.Add(item);
            }
            menu.Items.Add(themeRoot);
            menu.Items.Add(new ToolStripSeparator());

            // --- [系统硬件详情] ---
            var btnHardware = new ToolStripMenuItem(LanguageManager.T("Menu.HardwareInfo"));
            btnHardware.Image = Properties.Resources.HardwareInfo;
            btnHardware.Click += (s, e) =>
            {
                // 每次点击都 new 一个新的，关闭即销毁，不占用后台内存。
                var hwForm = new HardwareInfoForm();
                hwForm.Show(); // 非模态显示，允许用户一边看一边操作其他
            };
            menu.Items.Add(btnHardware);

            menu.Items.Add(new ToolStripSeparator());

            // 网络测速 (独立窗口，保持原样)
            var speedWindow = new ToolStripMenuItem(LanguageManager.T("Menu.Speedtest"));
            speedWindow.Image = Properties.Resources.NetworkIcon;
            speedWindow.Click += (_, __) =>
            {
                var f = new SpeedTestForm();
                f.Show();
            };
            menu.Items.Add(speedWindow);

            // 监控历史 (独立窗口，轻量自绘)
            var trendItem = new ToolStripMenuItem(LanguageManager.T("Menu.MonitorHistory"));
            trendItem.Image = Properties.Resources.MonitorHistory;
            trendItem.Click += (_, __) =>
            {
                foreach (Form openForm in Application.OpenForms)
                {
                    if (openForm is HardwareTrendForm)
                    {
                        openForm.Activate();
                        return;
                    }
                }

                var trendForm = new HardwareTrendForm(cfg);
                trendForm.Show();
            };
            menu.Items.Add(trendItem);

            // 历史流量统计 (独立窗口，保持原样)
            var trafficItem = new ToolStripMenuItem(LanguageManager.T("Menu.Traffic"));
            trafficItem.Image = Properties.Resources.TrafficIcon;
            trafficItem.Click += (_, __) =>
            {
                var formHistory = new TrafficHistoryForm(cfg);
                formHistory.Show();
            };
            menu.Items.Add(trafficItem);
            menu.Items.Add(new ToolStripSeparator());
        }

        // ==================================================================================
        // 分段 5：设置中心入口
        // ==================================================================================
        private static void AddSettingsItem(MenuContext ctx, ContextMenuStrip menu)
        {
            var itemSettings = new ToolStripMenuItem(LanguageManager.T("Menu.SettingsPanel"));
            itemSettings.Image = Properties.Resources.Settings;
            itemSettings.Font = new Font(itemSettings.Font, FontStyle.Bold);

            itemSettings.Click += (_, __) =>
            {
                try
                {
                    // 打开设置窗口
                    using (var f = new LiteMonitor.src.UI.SettingsForm(ctx.Cfg, ctx.Ui, ctx.Form))
                    {
                        if (!string.IsNullOrEmpty(ctx.TargetPage)) f.SwitchPage(ctx.TargetPage);
                        f.ShowDialog(ctx.Form);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("设置面板启动失败: " + ex.Message);
                }
            };
            menu.Items.Add(itemSettings);

            menu.Items.Add(new ToolStripSeparator());
        }

        // ==================================================================================
        // 分段 6：语言切换
        // ==================================================================================
        private static ToolStripMenuItem BuildLanguageRoot(MenuContext ctx)
        {
            var cfg = ctx.Cfg;
            var langRoot = new ToolStripMenuItem(LanguageManager.T("Menu.Language"));
            string langDir = Path.Combine(AppContext.BaseDirectory, "resources/lang");

            if (Directory.Exists(langDir))
            {
                // [Optimization] Shared handler
                EventHandler onLangClick = (s, e) =>
                {
                    if (s is ToolStripMenuItem item && item.Tag is string code)
                    {
                        cfg.Language = code;
                        cfg.Save();
                        AppActions.ApplyLanguage(cfg, ctx.Ui, ctx.Form);
                    }
                };

                foreach (var file in Directory.EnumerateFiles(langDir, "*.json"))
                {
                    string code = Path.GetFileNameWithoutExtension(file);

                    var item = new ToolStripMenuItem(code.ToUpper())
                    {
                        Checked = cfg.Language.Equals(code, StringComparison.OrdinalIgnoreCase),
                        Tag = code
                    };
                    item.Click += onLangClick;

                    langRoot.DropDownItems.Add(item);
                }
            }

            return langRoot;
        }

        // ==================================================================================
        // 分段 7：开机启动
        // ==================================================================================
        private static ToolStripMenuItem BuildAutoStartItem(MenuContext ctx)
        {
            var cfg = ctx.Cfg;
            var autoStart = new ToolStripMenuItem(LanguageManager.T("Menu.AutoStart"))
            {
                Checked = cfg.AutoStart,
                CheckOnClick = true
            };
            autoStart.CheckedChanged += (_, __) =>
            {
                cfg.AutoStart = autoStart.Checked;
                cfg.Save();
                // ★ 统一调用
                AppActions.ApplyAutoStart(cfg);
            };
            return autoStart;
        }

        // ==================================================================================
        // 分段 8：更多 (任务管理器 / 重启资源管理器 / 定时关机 / 重启软件)
        // ==================================================================================
        private static ToolStripMenuItem BuildMoreRoot(MenuContext ctx)
        {
            var form = ctx.Form;
            var moreRoot = new ToolStripMenuItem(LanguageManager.T("Menu.More"));

            // 1. 打开任务管理器
            var itemTaskMgr = new ToolStripMenuItem(LanguageManager.T("Menu.ActionTaskMgr"));
            itemTaskMgr.Click += (_, __) => SystemActions.OpenTaskManager();
            moreRoot.DropDownItems.Add(itemTaskMgr);

            // 2. 重启资源管理器
            var itemRestartExp = new ToolStripMenuItem(LanguageManager.T("Menu.RestartExplorer"));
            itemRestartExp.Click += (_, __) => SystemActions.RestartExplorer();
            moreRoot.DropDownItems.Add(itemRestartExp);

            moreRoot.DropDownItems.Add(new ToolStripSeparator());

            // 2.1 刷新桌面图标缓存
            var itemRefreshIcons = new ToolStripMenuItem(LanguageManager.T("Menu.RefreshIcons"));
            itemRefreshIcons.Click += (_, __) => SystemActions.RefreshIconCache();
            moreRoot.DropDownItems.Add(itemRefreshIcons);

            // 2.4 清理临时文件
            var itemCleanTemp = new ToolStripMenuItem(LanguageManager.T("Menu.CleanTemp"));
            itemCleanTemp.Click += async (_, __) => await SystemActions.CleanTempFilesAsync();
            moreRoot.DropDownItems.Add(itemCleanTemp);

            moreRoot.DropDownItems.Add(new ToolStripSeparator());

            // 3. 禁止自动休眠 (Toggle)
            var itemNoSleep = new ToolStripMenuItem(LanguageManager.T("Menu.PreventSleep"))
            {
                Checked = SystemActions.IsPreventSleep,
                CheckOnClick = true
            };
            itemNoSleep.Click += (_, __) =>
            {
                SystemActions.TogglePreventSleep();
                itemNoSleep.Checked = SystemActions.IsPreventSleep;
            };
            moreRoot.DropDownItems.Add(itemNoSleep);

            // 4. 关闭显示器
            var itemOffScreen = new ToolStripMenuItem(LanguageManager.T("Menu.TurnOffMonitor"));
            itemOffScreen.Click += (_, __) => SystemActions.TurnOffMonitor(form.Handle);
            moreRoot.DropDownItems.Add(itemOffScreen);

            // 5. 定时关机 (Submenu)
            moreRoot.DropDownItems.Add(BuildShutdownSubmenu());

            moreRoot.DropDownItems.Add(new ToolStripSeparator());

            // 6. 重启软件 (App)
            var itemRestartApp = new ToolStripMenuItem(LanguageManager.T("Menu.RestartApp"));
            itemRestartApp.Click += (_, __) => SystemActions.RestartApplication();
            moreRoot.DropDownItems.Add(itemRestartApp);

            return moreRoot;
        }

        private static ToolStripMenuItem BuildShutdownSubmenu()
        {
            var itemShutdown = new ToolStripMenuItem(LanguageManager.T("Menu.ScheduledShutdown"));

            void AddShutdownItem(string label, int seconds)
            {
                var sub = new ToolStripMenuItem(label);
                sub.Click += (_, __) => SystemActions.ScheduleShutdown(seconds);
                itemShutdown.DropDownItems.Add(sub);
            }

            int[] minutes = { 5, 10, 15, 30, 45 };
            foreach (var m in minutes)
            {
                AddShutdownItem(m + " " + LanguageManager.T("Menu.MinutesLater"), m * 60);
            }

            int[] hours = { 1, 2, 3, 4, 5, 6, 8, 10, 12, 24 };
            foreach (var h in hours)
            {
                AddShutdownItem(h + " " + LanguageManager.T("Menu.HoursLater"), h * 3600);
            }

            itemShutdown.DropDownItems.Add(new ToolStripSeparator());
            AddShutdownItem(LanguageManager.T("Menu.CancelShutdown"), 0);

            return itemShutdown;
        }

        // ==================================================================================
        // 分段 9：退出
        // ==================================================================================
        private static ToolStripMenuItem BuildExitItem(MenuContext ctx)
        {
            var itemExit = new ToolStripMenuItem(LanguageManager.T("Menu.Exit"));
            itemExit.Click += (_, __) => ctx.Form.Close();
            return itemExit;
        }
    }
}
