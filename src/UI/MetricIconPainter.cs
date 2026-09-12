using LiteMonitor.src.Core;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LiteMonitor
{
    /// <summary>
    /// 任务栏指标矢量图标（v2：极简加粗线性）。纯 GDI+ 程序化绘制，无图片资产，任意 DPI 锐利。
    ///
    /// 【五条设计规则 —— 改动前必读】
    /// 1. 统一光学框：24×24 设计网格，几何基准框 3.2..20.8，圆头笔帽外扩 1.2 ⇒ 墨迹恰好落在 2..22（20u）。
    ///    每个字形都要把这条主对角线撑满、重心落在 (12,12)。这是"一排图标看着一样大"的唯一来源。
    /// 2. 全局唯一描边 2.4u（15px 图标 ≈ 1.5 物理像素）。禁止单个图标私自调粗细——上一版就是因此发灰、发乱。
    /// 3. 一笔一意：每个图标 ≤3 个视觉元素、≤8 条线段。15px 下多一笔就是一团灰（旧版 GPU 的挡板+支脚、
    ///    风扇三弯叶、水泵双轮廓都是这样糊掉的）。
    /// 4. 实心只用于"核心"：芯片/显存核心、电池电量、风扇轮毂、温度计玻泡、水泵水滴、
    ///    闪电（闪电是唯一必须实心的字形，理由见 DrawBolt）。其余一律描边。
    /// 5. 语义取"量纲优先于组件"：CPU.Temp 画温度计，不画芯片。否则双行列的上下两行会出现同款图标，
    ///    只能靠数值单位才能分辨。组件图标（芯片/显卡/内存条）只留给没有专属量纲符号的"占用率"。
    /// </summary>
    public static class MetricIconPainter
    {
        public enum IconId
        {
            Chip,        // CPU 占用：方芯 + 四向引脚 + 实心核心
            Gpu,         // GPU 占用：长卡 + 单风扇
            Dimm,        // 内存占用：内存条 + 金手指
            Vram,        // 显存：立式 IC + 两侧引脚
            Thermo,      // 温度（一切 *Temp）：温度计
            Gauge,       // 频率（一切 *Clock）：表盘
            Bolt,        // 功耗（一切 *Power）：闪电
            Wave,        // 电压/电流：正弦波
            Fan,         // 转速 Fan：环形 + 三叶
            Pump,        // 水泵 Pump：环形 + 水滴
            Up, Down,    // 网络上传/下载：裸箭头
            DiskRead,    // 磁盘读：托盘 + 向上箭头
            DiskWrite,   // 磁盘写：托盘 + 向下箭头
            Disk,        // 通用存储：圆柱
            Battery,     // 电量（按真实百分比填充）
            Clock,       // 时间
            Stopwatch,   // 开机时长
            Globe,       // 公网 IP
            Display,     // 主机名
            Crosshair,   // FPS：准星
            Pulse        // 兜底 / 插件：脉搏线
        }

        /// <summary>全部图标（供预览、测试遍历）。</summary>
        public static readonly IconId[] All = (IconId[])Enum.GetValues(typeof(IconId));

        private const float Grid = 24f;         // 设计网格
        private const float StrokeUnits = 2.4f; // 唯一描边宽（网格单位）

        // =========================================================
        // 尺寸与间距（布局测量与渲染必须共用同一公式，否则列宽会抖）
        // =========================================================
        public static int IconSizeFor(Font? f)
        {
            if (f == null) return 15;
            return (int)Math.Clamp(MathF.Round(f.Height * 0.92f), 13, 20);
        }

        // =========================================================
        // key → 图标映射（量纲优先，组件兜底）
        // =========================================================
        public static IconId Resolve(string? key)
        {
            if (string.IsNullOrEmpty(key)) return IconId.Pulse;
            string k = key;

            if (k.Equals("FPS", StringComparison.OrdinalIgnoreCase)) return IconId.Crosshair;

            if (k.StartsWith("DASH.", StringComparison.OrdinalIgnoreCase))
            {
                switch (k.Substring(5).ToUpperInvariant())
                {
                    case "HOST": return IconId.Display;
                    case "TIME": return IconId.Clock;
                    case "UPTIME": return IconId.Stopwatch;
                    case "IP": return IconId.Globe;
                    default: return IconId.Pulse;
                }
            }

            // ---- 第一优先：量纲（保证同一列上下两行不会撞图） ----
            if (Has(k, "TEMP")) return IconId.Thermo;
            if (Has(k, "FAN")) return IconId.Fan;
            if (Has(k, "PUMP")) return IconId.Pump;
            if (Has(k, "POWER") || Has(k, "WATT")) return IconId.Bolt;
            // 注意不要加 "AMP" 这种短词：IndexOf 子串匹配会把 "Sample" 判成正弦波
            if (Has(k, "VOLTAGE") || Has(k, "VOLT") || Has(k, "CURRENT")) return IconId.Wave;
            if (Has(k, "CLOCK") || Has(k, "FREQ") || Has(k, "HZ")) return IconId.Gauge;

            // 方向类按"末段"判定：整串 EndsWith("UP") 会把插件的 "Backup" 判成上传，
            // 整串 IndexOf("READ") 会把 "Threads" 判成磁盘读——这两个误判是实测出来的。
            string suffix = k.Substring(k.LastIndexOf('.') + 1);
            if (suffix.Equals("READ", StringComparison.OrdinalIgnoreCase)) return IconId.DiskRead;
            if (suffix.Equals("WRITE", StringComparison.OrdinalIgnoreCase)) return IconId.DiskWrite;
            if (suffix.Equals("DOWN", StringComparison.OrdinalIgnoreCase) || Has(k, "DOWNLOAD")) return IconId.Down;
            if (suffix.Equals("UP", StringComparison.OrdinalIgnoreCase) || Has(k, "UPLOAD")) return IconId.Up;

            if (Has(k, "VRAM")) return IconId.Vram;
            if (Has(k, "PERCENT") && k.StartsWith("BAT", StringComparison.OrdinalIgnoreCase)) return IconId.Battery;

            // ---- 第二优先：组件身份（占用率这类没有专属量纲符号的指标） ----
            if (k.StartsWith("CPU", StringComparison.OrdinalIgnoreCase)) return IconId.Chip;
            if (k.StartsWith("GPU", StringComparison.OrdinalIgnoreCase)) return IconId.Gpu;
            if (Has(k, "MEM") || Has(k, "RAM")) return IconId.Dimm;
            if (Has(k, "DISK") || Has(k, "HDD") || Has(k, "SSD") || Has(k, "DRIVE")) return IconId.Disk;
            if (k.StartsWith("BAT", StringComparison.OrdinalIgnoreCase)) return IconId.Battery;

            // ---- 兜底：按类型推断（主板温度、机箱风扇、插件指标等） ----
            return MetricUtils.GetType(k) switch
            {
                MetricType.Temperature => IconId.Thermo,
                MetricType.Memory => IconId.Dimm,
                MetricType.Power => IconId.Bolt,
                MetricType.RPM => IconId.Fan,
                MetricType.Frequency => IconId.Gauge,
                MetricType.Voltage => IconId.Wave,
                MetricType.Current => IconId.Wave,
                MetricType.FPS => IconId.Crosshair,
                _ => IconId.Pulse
            };
        }

        private static bool Has(string s, string token) =>
            s.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        // =========================================================
        // 绘制入口
        // =========================================================
        /// <param name="fillPct">电池电量 0..1（&lt;0 表示不画电量条）</param>
        /// <param name="accent">电池充电中（图标内改画闪电；正常由数值的 ⚡ 后缀表达，故默认 false）</param>
        public static void Draw(Graphics g, IconId id, Rectangle rect, Color color,
                                float fillPct = -1f, bool accent = false)
        {
            int isz = Math.Min(rect.Width, rect.Height);
            if (isz <= 4) return;

            float s = isz / Grid;

            // 量化到 1/4 物理像素：避免出现 1.37px 这种"半灰"描边
            float dev = MathF.Max(1.2f, StrokeUnits * s);
            dev = MathF.Round(dev * 4f) / 4f;

            var oldTransform = g.Transform;
            var oldSmoothing = g.SmoothingMode;

            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // 以图标框中心对齐网格中心 (12,12)：左右抗锯齿对称，比左上角对齐更"实"
                g.TranslateTransform(rect.X + isz / 2f, rect.Y + isz / 2f);
                g.ScaleTransform(s, s);
                g.TranslateTransform(-Grid / 2f, -Grid / 2f);

                using var pen = new Pen(color, dev / s)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round
                };
                using var fill = new SolidBrush(color);

                switch (id)
                {
                    case IconId.Chip: DrawChip(g, pen, fill); break;
                    case IconId.Gpu: DrawGpu(g, pen, fill); break;
                    case IconId.Dimm: DrawDimm(g, pen); break;
                    case IconId.Vram: DrawVram(g, pen, fill); break;
                    case IconId.Thermo: DrawThermo(g, pen, fill); break;
                    case IconId.Gauge: DrawGauge(g, pen, fill); break;
                    case IconId.Bolt: DrawBolt(g, pen, fill); break;
                    case IconId.Wave: DrawWave(g, pen); break;
                    case IconId.Fan: DrawFan(g, pen, fill); break;
                    case IconId.Pump: DrawPump(g, pen, fill); break;
                    case IconId.Up: DrawArrow(g, pen, up: true, tray: false); break;
                    case IconId.Down: DrawArrow(g, pen, up: false, tray: false); break;
                    case IconId.DiskRead: DrawArrow(g, pen, up: true, tray: true); break;
                    case IconId.DiskWrite: DrawArrow(g, pen, up: false, tray: true); break;
                    case IconId.Disk: DrawDisk(g, pen); break;
                    case IconId.Battery: DrawBattery(g, pen, fill, fillPct, accent); break;
                    case IconId.Clock: DrawClock(g, pen); break;
                    case IconId.Stopwatch: DrawStopwatch(g, pen); break;
                    case IconId.Globe: DrawGlobe(g, pen); break;
                    case IconId.Display: DrawDisplay(g, pen); break;
                    case IconId.Crosshair: DrawCrosshair(g, pen); break;
                    default: DrawPulse(g, pen); break;
                }
            }
            finally
            {
                g.Transform = oldTransform;
                g.SmoothingMode = oldSmoothing;
            }
        }

        // =========================================================
        // 几何基元
        // =========================================================
        private static GraphicsPath Round(float x, float y, float w, float h, float r)
        {
            var path = new GraphicsPath();
            float d = r * 2f;
            if (d <= 0f || w < d || h < d)
            {
                path.AddRectangle(new RectangleF(x, y, w, h));
                return path;
            }
            path.AddArc(x, y, d, d, 180f, 90f);
            path.AddArc(x + w - d, y, d, d, 270f, 90f);
            path.AddArc(x + w - d, y + h - d, d, d, 0f, 90f);
            path.AddArc(x, y + h - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        private static GraphicsPath Poly(params PointF[] pts)
        {
            var path = new GraphicsPath();
            path.AddLines(pts);
            return path;
        }

        /// <summary>圆环（外径 2r，圆心 12,12），墨迹正好吃掉 2..22。</summary>
        private static void Ring(Graphics g, Pen p, float r) => g.DrawEllipse(p, 12f - r, 12f - r, r * 2f, r * 2f);

        // =========================================================
        // 各字形（坐标即设计网格；已核算墨迹框 = 2..22、重心 = 12,12）
        // =========================================================
        private static void DrawChip(Graphics g, Pen p, Brush f)
        {
            using var body = Round(6.2f, 6.2f, 11.6f, 11.6f, 2.2f);
            g.DrawPath(p, body);
            using var core = Round(10.2f, 10.2f, 3.6f, 3.6f, 1.1f);
            g.FillPath(f, core);

            // 四向引脚：把墨迹顶到 2..22，同时构成"芯片"的十字轮廓
            g.DrawLine(p, 12f, 3.2f, 12f, 6.4f);
            g.DrawLine(p, 12f, 17.6f, 12f, 20.8f);
            g.DrawLine(p, 3.2f, 12f, 6.4f, 12f);
            g.DrawLine(p, 17.6f, 12f, 20.8f, 12f);
        }

        private static void DrawGpu(Graphics g, Pen p, Brush f)
        {
            using var card = Round(3.2f, 4.8f, 17.6f, 14.4f, 2.8f);
            g.DrawPath(p, card);
            g.DrawEllipse(p, 7.4f, 7.4f, 9.2f, 9.2f);       // 风扇外圈
            g.FillEllipse(f, 10.6f, 10.6f, 2.8f, 2.8f);     // 轮毂
            // 旧版的挡板竖线 + 支脚已删：15px 下只是三根糊线，卡体+单风扇才是可读剪影
        }

        private static void DrawDimm(Graphics g, Pen p)
        {
            using var body = Round(3.2f, 5.6f, 17.6f, 9.6f, 1.8f);
            g.DrawPath(p, body);
            foreach (float x in new[] { 7.2f, 12f, 16.8f })  // 金手指
                g.DrawLine(p, x, 15.2f, x, 18.4f);
        }

        private static void DrawVram(Graphics g, Pen p, Brush f)
        {
            using var body = Round(6.2f, 4.8f, 11.6f, 14.4f, 2.2f);
            g.DrawPath(p, body);
            using var core = Round(10.0f, 10.0f, 4.0f, 4.0f, 1.2f);
            g.FillPath(f, core);
            foreach (float y in new[] { 9.0f, 15.0f })
            {
                g.DrawLine(p, 3.2f, y, 6.4f, y);
                g.DrawLine(p, 17.6f, y, 20.8f, y);
            }
        }

        private static void DrawThermo(Graphics g, Pen p, Brush f)
        {
            // 只留"管 + 实心玻泡"两笔：管内再画水银柱的话，15px 下管壁与水银会糊成一根实心棒
            using var tube = Round(9.0f, 3.4f, 6.0f, 12.6f, 3.0f);
            g.DrawPath(p, tube);
            g.FillEllipse(f, 6.6f, 11.2f, 10.8f, 10.8f);    // 玻泡：实心核心，撑起细长字形的分量
        }

        private static void DrawGauge(Graphics g, Pen p, Brush f)
        {
            const float cx = 12f, cy = 12.8f, r = 8.8f;
            g.DrawArc(p, cx - r, cy - r, r * 2f, r * 2f, 135f, 270f); // 270° 表盘，缺口朝下
            g.DrawLine(p, cx, cy, cx + 4.6f, cy - 5.6f);              // 指针
            g.FillEllipse(f, cx - 1.3f, cy - 1.3f, 2.6f, 2.6f);       // 轴心：只略粗于指针，再大就成中心墨块
        }

        private static void DrawBolt(Graphics g, Pen p, Brush f)
        {
            // 闪电必须实心：描边版的两根横杆间距只有 3.6u，2.4u 描边会把腰糊成一块墨。
            // 实测填充版覆盖度 ≈12%，比环形图标（20~26%）更轻，不会破坏整排重量。
            using var path = new GraphicsPath();
            path.AddPolygon(new[]
            {
                new PointF(14.4f, 2.8f), new PointF(6.0f, 11.2f), new PointF(11.0f, 11.2f),
                new PointF(9.6f, 21.2f), new PointF(18.0f, 12.8f), new PointF(13.0f, 12.8f)
            });
            g.FillPath(f, path);
        }

        private static void DrawWave(Graphics g, Pen p)
        {
            // 单周期正弦（两周期在小尺寸下会糊成一段乱线）；振幅取到墨迹高度 ≈10/15px，别让它比别的图标扁
            using var path = new GraphicsPath();
            path.AddBezier(3.6f, 12f, 5.4f, 3.4f, 9.6f, 3.4f, 12f, 12f);
            path.AddBezier(12f, 12f, 14.4f, 20.6f, 18.6f, 20.6f, 20.4f, 12f);
            g.DrawPath(p, path);
        }

        private static void DrawFan(Graphics g, Pen p, Brush f)
        {
            Ring(g, p, 8.8f);
            // 叶尖必须停在圆环内缘（8.8-1.2=7.6）以内：旧版叶尖 7.4+1.2=8.6 直接压在环上，糊成三个墨块
            for (int i = 0; i < 3; i++)
            {
                float a0 = (270f + i * 120f) * MathF.PI / 180f;
                float a1 = a0 + 26f * MathF.PI / 180f;
                float x0 = 12f + 2.6f * MathF.Cos(a0), y0 = 12f + 2.6f * MathF.Sin(a0);
                float xc = 12f + 6.4f * MathF.Cos(a0), yc = 12f + 6.4f * MathF.Sin(a0);
                float x1 = 12f + 6.2f * MathF.Cos(a1), y1 = 12f + 6.2f * MathF.Sin(a1);
                using var blade = new GraphicsPath();
                blade.AddBezier(x0, y0, xc, yc, xc, yc, x1, y1);
                g.DrawPath(p, blade);
            }
            g.FillEllipse(f, 10.2f, 10.2f, 3.6f, 3.6f);
        }

        private static void DrawPump(Graphics g, Pen p, Brush f)
        {
            Ring(g, p, 8.8f);
            using var drop = new GraphicsPath();
            drop.AddBezier(12f, 6.2f, 10.4f, 9.0f, 8.6f, 11.6f, 8.6f, 14.2f);
            drop.AddArc(8.6f, 11.0f, 6.8f, 6.8f, 180f, -180f);   // 底部半圆（负扫角 = 走下半圈）
            drop.AddBezier(15.4f, 14.2f, 15.4f, 11.6f, 13.6f, 9.0f, 12f, 6.2f);
            drop.CloseFigure();
            g.FillPath(f, drop);
        }

        private static void DrawArrow(Graphics g, Pen p, bool up, bool tray)
        {
            const float headLen = 6.0f, halfW = 6.4f;
            float tipY, tailY, trayTopY = 0f;

            if (tray)
            {
                trayTopY = 14.6f;
                using var holder = Poly(
                    new PointF(3.6f, trayTopY), new PointF(3.6f, 20.8f),
                    new PointF(20.4f, 20.8f), new PointF(20.4f, trayTopY)); // 托盘：一条折线代替旧版三根线
                g.DrawPath(p, holder);
                tipY = up ? 3.2f : 13.6f;
                tailY = up ? 13.6f : 3.2f;
            }
            else
            {
                tipY = up ? 3.2f : 20.8f;
                tailY = up ? 20.8f : 3.2f;
            }

            float wingY = up ? tipY + headLen : tipY - headLen;
            g.DrawLine(p, 12f, tailY, 12f, tipY);
            using var head = Poly(
                new PointF(12f - halfW, wingY), new PointF(12f, tipY), new PointF(12f + halfW, wingY));
            g.DrawPath(p, head);
        }

        private static void DrawDisk(Graphics g, Pen p)
        {
            g.DrawEllipse(p, 3.6f, 3.4f, 16.8f, 5.2f);              // 顶面
            g.DrawLine(p, 3.6f, 6.0f, 3.6f, 17.8f);
            g.DrawLine(p, 20.4f, 6.0f, 20.4f, 17.8f);
            g.DrawArc(p, 3.6f, 15.2f, 16.8f, 5.2f, 0f, 180f);        // 底弧（经 90° 走下缘）
        }

        private static void DrawBattery(Graphics g, Pen p, Brush f, float pct, bool charging)
        {
            using var shell = Round(3.2f, 6.0f, 16.2f, 12.0f, 2.8f);
            g.DrawPath(p, shell);
            using var nub = Round(20.0f, 10.4f, 2.0f, 3.2f, 1.0f);   // 正极凸头（实心）
            g.FillPath(f, nub);

            if (charging)
            {
                using var bolt = new GraphicsPath();
                bolt.AddPolygon(new[]
                {
                    new PointF(13.0f, 7.0f), new PointF(7.6f, 13.2f), new PointF(11.2f, 13.2f),
                    new PointF(10.2f, 17.0f), new PointF(15.6f, 10.8f), new PointF(12.0f, 10.8f)
                });
                g.FillPath(f, bolt);
            }
            else if (pct >= 0f)
            {
                const float x0 = 5.0f, y0 = 7.8f, h = 8.4f, maxW = 12.6f;
                float w = MathF.Max(1.6f, maxW * Math.Clamp(pct, 0f, 1f));
                using var bar = Round(x0, y0, w, h, MathF.Min(1.4f, w / 2f));
                g.FillPath(f, bar);
            }
        }

        private static void DrawClock(Graphics g, Pen p)
        {
            Ring(g, p, 8.8f);
            g.DrawLine(p, 12f, 6.8f, 12f, 12f);
            g.DrawLine(p, 12f, 12f, 16.4f, 15.0f);
        }

        private static void DrawStopwatch(Graphics g, Pen p)
        {
            g.DrawEllipse(p, 4.6f, 6.0f, 14.8f, 14.8f);
            g.DrawLine(p, 12f, 3.4f, 12f, 6.4f);        // 顶部按钮
            g.DrawLine(p, 12f, 13.4f, 15.4f, 10.6f);    // 指针
        }

        private static void DrawGlobe(Graphics g, Pen p)
        {
            Ring(g, p, 8.8f);
            g.DrawLine(p, 3.2f, 12f, 20.8f, 12f);       // 赤道
            g.DrawEllipse(p, 7.4f, 3.2f, 9.2f, 17.6f);  // 经线
        }

        private static void DrawDisplay(Graphics g, Pen p)
        {
            using var screen = Round(3.2f, 4.2f, 17.6f, 12.6f, 2.4f);
            g.DrawPath(p, screen);
            g.DrawLine(p, 12f, 16.4f, 12f, 19.8f);      // 支架
            g.DrawLine(p, 7.6f, 20.4f, 16.4f, 20.4f);   // 底座
        }

        private static void DrawCrosshair(Graphics g, Pen p)
        {
            g.DrawEllipse(p, 4.6f, 4.6f, 14.8f, 14.8f); // 旧版中心点已删：小尺寸下环+四刻度已经够读
            g.DrawLine(p, 12f, 3.2f, 12f, 6.6f);
            g.DrawLine(p, 12f, 17.4f, 12f, 20.8f);
            g.DrawLine(p, 3.2f, 12f, 6.6f, 12f);
            g.DrawLine(p, 17.4f, 12f, 20.8f, 12f);
        }

        private static void DrawPulse(Graphics g, Pen p)
        {
            using var path = Poly(
                new PointF(3.2f, 12.8f), new PointF(7.6f, 12.8f), new PointF(10.0f, 6.0f),
                new PointF(14.0f, 18.0f), new PointF(16.4f, 12.8f), new PointF(20.8f, 12.8f));
            g.DrawPath(p, path);
        }
    }
}
