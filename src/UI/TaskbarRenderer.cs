using LiteMonitor.src.Core;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace LiteMonitor
{
    /// <summary>
    /// 任务栏渲染器（仅负责绘制，不再负责布局）
    /// </summary>
    public static class TaskbarRenderer
    {
        //private static readonly Settings _settings = Settings.Load();
        
        // 字体缓存 - 直接初始化，避免每次渲染都创建字体
        private static Font? _cachedFont = null;

        // 浅色主题
        private static readonly Color LABEL_LIGHT = Color.FromArgb(20, 20, 20);
        private static readonly Color SAFE_LIGHT = Color.FromArgb(0x00, 0x80, 0x40);
        private static readonly Color WARN_LIGHT = Color.FromArgb(0xB5, 0x75, 0x00);
        private static readonly Color CRIT_LIGHT = Color.FromArgb(0xC0, 0x30, 0x30);

        // 深色主题
        private static readonly Color LABEL_DARK = Color.White;
        private static readonly Color SAFE_DARK = Color.FromArgb(0x66, 0xFF, 0x99);
        private static readonly Color WARN_DARK = Color.FromArgb(0xFF, 0xD6, 0x66);
        private static readonly Color CRIT_DARK = Color.FromArgb(0xFF, 0x66, 0x66);

        // ★★★ [新增] 自定义颜色缓存 ★★★
        private static bool _useCustom = false;
        private static Color _cLabel, _cSafe, _cWarn, _cCrit;

        // ★★★ [新增] 图标模式缓存（ReloadStyle 时统一刷新） ★★★
        private static bool _useIcons = false;
        private static int _iconSize = 15;
        private static int _iconGap = 8;

        // ★★★ [新增] 极简的核心：手动刷新缓存 ★★★
        // 在 UIController 初始化或配置变更时调用它
        public static void ReloadStyle(Settings cfg)
        {
            var s = cfg.GetStyle();

            // ★★★ 修复：这里持有的字体是 UIUtils.GetFont 共享缓存的实例，绝不能 Dispose ★★★
            // Dispose 后缓存字典仍会把这个已释放实例交回来，GDI+ DrawString 会抛
            // "Parameter is not valid"（GDI TextRenderer 恰好容忍，所以旧版未暴露）。
            // 字体生命周期统一归 UIUtils（ClearBrushCache 释放并清缓存）。
            _cachedFont = UIUtils.GetFont(s.Font, s.Size, s.Bold);

            // 图标模式：尺寸/间距随字体与 DPI 缩放（与 HorizontalLayout 测量共用公式）
            _useIcons = cfg.TaskbarUseIcons;
            if (_useIcons)
            {
                _iconSize = MetricIconPainter.IconSizeFor(_cachedFont);
                using var g = Graphics.FromHwnd(IntPtr.Zero);
                _iconGap = (int)MathF.Round(s.Inner * g.DpiX / 96f);
            }

            // 颜色依然允许自定义
            _useCustom = cfg.TaskbarCustomStyle;
            if (_useCustom)
            {
                try {
                    _cLabel = ColorTranslator.FromHtml(cfg.TaskbarColorLabel);
                    _cSafe = ColorTranslator.FromHtml(cfg.TaskbarColorSafe);
                    _cWarn = ColorTranslator.FromHtml(cfg.TaskbarColorWarn);
                    _cCrit = ColorTranslator.FromHtml(cfg.TaskbarColorCrit);
                } catch {
                    // 容错：如果解析失败，回退到默认
                    _useCustom = false; 
                }
            }
        }

        public static void Render(Graphics g, List<Column> cols, bool light, bool alphaSurface = false) // <--- 新的
        {
            // [防空策略] 万一还没人调用 ReloadStyle，就自己兜底初始化一次
            if (_cachedFont == null)
            {
                // 兜底：读磁盘配置（仅第一次）
                ReloadStyle(Settings.Load());
            }

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            if (!alphaSurface) g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            else g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            // 使用传入的 light 参数，避免每次都查询系统主题瓠
            //bool light = IsSystemLight();

            foreach (var col in cols)
            {
                // ★★★ [新增]：如果只有 Top 没有 Bottom，强制使用全高绘制（居中）
                if (col.Top != null && col.Bottom == null && col.Bounds != Rectangle.Empty)
                {
                    DrawItem(g, col.Top, col.Bounds, light, alphaSurface);
                    continue; // 处理完这个特殊情况直接跳过本次循环
                }

                if (col.BoundsTop != Rectangle.Empty && col.Top != null)
                    DrawItem(g, col.Top, col.BoundsTop, light, alphaSurface);

                if (col.BoundsBottom != Rectangle.Empty && col.Bottom != null)
                    DrawItem(g, col.Bottom, col.BoundsBottom, light, alphaSurface);
            }
        }

        // =================================================================
        // 毛玻璃面板背景（Mac 风格：圆角 + 顶部微渐变高光 + 1px 描边）
        // =================================================================
        // alpha 表面专用文字格式：GDI+ 才能正确写 alpha（GDI TextRenderer 会把像素 alpha 清零导致文字消失）
        private static readonly StringFormat _sfNear = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center
        };
        private static readonly StringFormat _sfFar = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Center
        };

        public static void RenderGlass(Graphics g, int w, int h, Color back, Color border)
        {
            int radius = Math.Max(4, Math.Min(h / 2 - 2, 12));
            using var path = RoundedRect(0, 0, w, h, radius);

            // 顶部轻微提亮的垂直渐变，模拟玻璃高光
            Color top = Color.FromArgb(back.A,
                Math.Min(255, back.R + 14),
                Math.Min(255, back.G + 14),
                Math.Min(255, back.B + 14));
            using var brush = new LinearGradientBrush(new Rectangle(0, 0, w, h), top, back, 90f);
            g.FillPath(brush, path);

            using var pen = new Pen(border);
            g.DrawPath(pen, path);
        }

        private static GraphicsPath RoundedRect(int x, int y, int w, int h, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void DrawItem(Graphics g, MetricItem item, Rectangle rc, bool light, bool alphaSurface = false)
        {
            // ★★★ 优化：直接使用缓存的 ShortLabel，避免每帧生成 Key 和查询字典 ★★★
            string label = item.ShortLabel;

            // ★★★ 修复：如果 ShortLabel 被显式设为空格或空，则视为隐藏标签 ★★★
            // 适用于 IP/Dashboard 文本，直接绘制 Value (左对齐)
            bool hideLabel = (string.IsNullOrEmpty(label) || label == " ");

            // 如果不是隐藏，且为空，则回退到 Label 或 Key
            if (!hideLabel)
            {
                if (string.IsNullOrEmpty(label)) label = item.Label;
                if (string.IsNullOrEmpty(label)) label = item.Key;
            }

            string value = item.GetFormattedText(true);

            // 直接使用缓存的字体，不再 new Font
            Font font = _cachedFont!;

            Color labelColor, valueColor;

            // ★★★ [修改] 颜色选择逻辑 ★★★
            if (_useCustom)
            {
                // 自定义模式：忽略系统明暗，强制使用自定义色
                labelColor = _cLabel;
                valueColor = GetCustomStateColor(item.CachedColorState);
            }
            else
            {
                // 原有模式
                labelColor = light ? LABEL_LIGHT : LABEL_DARK;
                valueColor = GetStateColor(item.CachedColorState, light);
            }

            // ★★★ 修复：如果开启了隐藏标签 (如 IP/Dashboard)，则仅绘制 Value (左对齐) ★★★
            if (hideLabel)
            {
                // 图标模式：即使标签被隐藏（IP/时间等"纯文字"项），也给出组件图标
                if (_useIcons)
                {
                    DrawMetricIcon(g, item, rc, labelColor);
                    var vrc = new Rectangle(rc.X + _iconSize + _iconGap, rc.Y,
                        Math.Max(0, rc.Width - _iconSize - _iconGap), rc.Height);
                    if (alphaSurface)
                    {
                        DrawStringSafe(g, value, vrc, valueColor, _sfNear);
                        return;
                    }
                    TextRenderer.DrawText(
                        g, value, font, vrc, valueColor,
                        TextFormatFlags.Left |
                        TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding |
                        TextFormatFlags.NoClipping
                    );
                    return;
                }

                if (alphaSurface)
                {
                    DrawStringSafe(g, value, rc, valueColor, _sfNear);
                    return;
                }
                TextRenderer.DrawText(
                    g, value, font, rc, valueColor,
                    TextFormatFlags.Left |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding |
                    TextFormatFlags.NoClipping
                );
                return;
            }

            // ★★★ [新增] 图标模式：以组件图标替代文字标签，数值保持状态色右对齐 ★★★
            if (_useIcons)
            {
                DrawMetricIcon(g, item, rc, labelColor);

                if (alphaSurface)
                {
                    DrawStringSafe(g, value, rc, valueColor, _sfFar);
                    return;
                }
                TextRenderer.DrawText(
                    g, value, font, rc, valueColor,
                    TextFormatFlags.Right |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding |
                    TextFormatFlags.NoClipping
                );
                return;
            }

            // Label 左对齐 / Value 右对齐（alpha 表面必须走 GDI+，GDI 会清像素 alpha）
            if (alphaSurface)
            {
                DrawStringSafe(g, label, rc, labelColor, _sfNear);
                DrawStringSafe(g, value, rc, valueColor, _sfFar);
                return;
            }

            // Label 左对齐
            TextRenderer.DrawText(
                g, label, font, rc, labelColor,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoClipping
            );

            // Value 右对齐
            TextRenderer.DrawText(
                g, value, font, rc, valueColor,
                TextFormatFlags.Right |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoClipping
            );
        }

        // alpha 表面用画刷缓存：GDI+ DrawString 每帧 new SolidBrush 会产生 GC 压力
        private static SolidBrush? _cachedBrush;
        private static Color _cachedBrushColor = Color.Empty;
        private static SolidBrush GetCachedBrush(Color c)
        {
            if (_cachedBrush == null || _cachedBrushColor != c)
            {
                _cachedBrush?.Dispose();
                _cachedBrush = new SolidBrush(c);
                _cachedBrushColor = c;
            }
            return _cachedBrush;
        }

        // alpha 表面专用：GDI+ 对已释放的 Font 会直接抛 "Parameter is not valid"（GDI 会容忍）。
        // 自愈策略：重建字体后重试一次，仍失败则跳过该项并记日志，绝不让单条文字拖垮整个任务栏。
        private static void DrawStringSafe(Graphics g, string text, Rectangle rc, Color color, StringFormat sf)
        {
            var rf = new RectangleF(rc.X, rc.Y, rc.Width, rc.Height);
            try
            {
                g.DrawString(text, _cachedFont!, GetCachedBrush(color), rf, sf);
            }
            catch (ArgumentException)
            {
                ReloadStyle(Settings.Load());
                try
                {
                    g.DrawString(text, _cachedFont!, GetCachedBrush(color), rf, sf);
                }
                catch (Exception ex)
                {
                    Log.Error($"[Taskbar] 玻璃模式文字绘制失败(已跳过) text='{text}'", ex);
                }
            }
        }
        // [新增] 辅助：根据状态快速获取颜色 (替代原来的 PickColor)
        private static Color GetStateColor(int state, bool light)
        {
            if (state == 2) return light ? CRIT_LIGHT : CRIT_DARK;
            if (state == 1) return light ? WARN_LIGHT : WARN_DARK;
            return light ? SAFE_LIGHT : SAFE_DARK;
        }

        // ★★★ [新增] 图标绘制：尺寸自适应行高，电池按真实电量填充 ★★★
        // 充电状态不复述：值文本已带 "⚡" 后缀（MetricUtils），图标内再画会重复
        private static void DrawMetricIcon(Graphics g, MetricItem item, Rectangle rc, Color color)
        {
            var id = MetricIconPainter.Resolve(item.Key);
            float fill = id == MetricIconPainter.IconId.Battery ? (float)item.CachedPercent : -1f;

            int isz = Math.Min(_iconSize, Math.Max(8, rc.Height));
            var irc = new Rectangle(rc.X, rc.Y + (rc.Height - isz) / 2, isz, isz);
            MetricIconPainter.Draw(g, id, irc, color, fill);
        }

        // [新增] 辅助：自定义模式
        private static Color GetCustomStateColor(int state)
        {
            if (state == 2) return _cCrit;
            if (state == 1) return _cWarn;
            return _cSafe;
        }
    }
}
