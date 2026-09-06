using LiteMonitor.src.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LiteMonitor
{
    /// <summary>
    /// 任务栏指标矢量图标（24×24 设计网格，2u 圆角描边，Fluent 风格）。
    /// 纯 GDI+ 程序化绘制：无资产文件，任意 DPI 下锐利。
    /// 图标编码"组件身份"（CPU/GPU/内存/网络/电池），指标类型由数值单位表达；
    /// 双行列的上下两行因此共享同一组件图标。
    /// </summary>
    public static class MetricIconPainter
    {
        public enum IconId
        {
            Chip,       // CPU：方形芯片 + 内核 + 引脚
            Gpu,        // GPU：显卡 + 风扇 + 挡板
            Dimm,       // 内存：内存条
            Vram,       // 显存：贴片 IC
            Thermo,     // 温度：温度计（主板/磁盘等无组件图标时）
            Gauge,      // 频率：仪表盘
            Bolt,       // 功耗：闪电
            Wave,       // 电压/电流：正弦波
            Fan,        // 风扇
            Pump,       // 水泵：圆环 + 水滴
            Up, Down,   // 网络上传/下载：箭头
            DiskRead, DiskWrite, // 磁盘读/写：托盘箭头
            Battery,    // 电池（电量动态填充，充电显示闪电）
            Clock,      // 时间
            Stopwatch,  // 开机时长
            Globe,      // IP
            Display,    // 主机名
            Gamepad,    // FPS
            Pulse       // 插件/未知：脉搏线（"监控"的身份符号）
        }

        private const float StrokeW = 2f; // 网格单位描边宽

        // =========================================================
        // 尺寸与间距（布局与渲染必须共用同一公式，否则宽度会抖动）
        // =========================================================
        public static int IconSizeFor(Font f)
        {
            if (f == null) return 15;
            return (int)Math.Clamp(MathF.Round(f.Height * 0.88f), 12, 20);
        }

        // =========================================================
        // key → 图标映射
        // =========================================================
        public static IconId Resolve(string key)
        {
            if (string.IsNullOrEmpty(key)) return IconId.Pulse;
            string k = key;

            if (k.Equals("FPS", StringComparison.OrdinalIgnoreCase)) return IconId.Gamepad;

            if (k.StartsWith("BAT", StringComparison.OrdinalIgnoreCase)) return IconId.Battery;

            if (k.StartsWith("DASH.", StringComparison.OrdinalIgnoreCase))
            {
                string d = k.Substring(5);
                if (d.Equals("HOST", StringComparison.OrdinalIgnoreCase)) return IconId.Display;
                if (d.Equals("Time", StringComparison.OrdinalIgnoreCase)) return IconId.Clock;
                if (d.Equals("Uptime", StringComparison.OrdinalIgnoreCase)) return IconId.Stopwatch;
                if (d.Equals("IP", StringComparison.OrdinalIgnoreCase)) return IconId.Globe;
                return IconId.Pulse;
            }

            if (k.IndexOf("VRAM", StringComparison.OrdinalIgnoreCase) >= 0) return IconId.Vram;
            if (k.IndexOf("MEM", StringComparison.OrdinalIgnoreCase) >= 0) return IconId.Dimm;
            if (k.StartsWith("CPU", StringComparison.OrdinalIgnoreCase)) return IconId.Chip;
            if (k.StartsWith("GPU", StringComparison.OrdinalIgnoreCase)) return IconId.Gpu;
            if (k.IndexOf("TEMP", StringComparison.OrdinalIgnoreCase) >= 0) return IconId.Thermo;
            if (k.IndexOf("CLOCK", StringComparison.OrdinalIgnoreCase) >= 0) return IconId.Gauge;
            if (k.IndexOf("FAN", StringComparison.OrdinalIgnoreCase) >= 0) return IconId.Fan;
            if (k.IndexOf("PUMP", StringComparison.OrdinalIgnoreCase) >= 0) return IconId.Pump;
            if (k.IndexOf("POWER", StringComparison.OrdinalIgnoreCase) >= 0) return IconId.Bolt;
            if (k.IndexOf("VOLTAGE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                k.IndexOf("CURRENT", StringComparison.OrdinalIgnoreCase) >= 0) return IconId.Wave;

            if (k.EndsWith("UP", StringComparison.OrdinalIgnoreCase) ||
                k.IndexOf("UPLOAD", StringComparison.OrdinalIgnoreCase) >= 0) return IconId.Up;
            if (k.EndsWith("DOWN", StringComparison.OrdinalIgnoreCase) ||
                k.IndexOf("DOWNLOAD", StringComparison.OrdinalIgnoreCase) >= 0) return IconId.Down;

            if (k.IndexOf("DISK", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (k.IndexOf("READ", StringComparison.OrdinalIgnoreCase) >= 0) return IconId.DiskRead;
                if (k.IndexOf("WRITE", StringComparison.OrdinalIgnoreCase) >= 0) return IconId.DiskWrite;
                return IconId.DiskRead;
            }

            // 类型兜底（主板温度、机箱风扇、未知插件等）
            return MetricUtils.GetType(k) switch
            {
                MetricType.Temperature => IconId.Thermo,
                MetricType.Memory => IconId.Dimm,
                MetricType.Power => IconId.Bolt,
                MetricType.RPM => IconId.Fan,
                MetricType.Frequency => IconId.Gauge,
                MetricType.Voltage => IconId.Wave,
                MetricType.Current => IconId.Wave,
                MetricType.FPS => IconId.Gamepad,
                _ => IconId.Pulse
            };
        }

        // =========================================================
        // 绘制入口
        // =========================================================
        // fillPct: 电池电量 0..1（<0 表示不填充）；accent: 充电（电池内画闪电）
        public static void Draw(Graphics g, IconId id, Rectangle rect, Color color, float fillPct = -1f, bool accent = false)
        {
            if (rect.Width <= 2 || rect.Height <= 2) return;

            float scale = rect.Width / 24f;
            var old = g.Transform;
            var oldSmooth = g.SmoothingMode;

            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TranslateTransform(rect.X, rect.Y);
                g.ScaleTransform(scale, scale);

                using var pen = CreatePen(color, StrokeW, scale);
                using var fill = new SolidBrush(color);

                switch (id)
                {
                    case IconId.Chip: DrawChip(g, pen, fill); break;
                    case IconId.Gpu: DrawGpu(g, pen, fill); break;
                    case IconId.Dimm: DrawDimm(g, pen); break;
                    case IconId.Vram: DrawVram(g, pen); break;
                    case IconId.Thermo: DrawThermo(g, pen); break;
                    case IconId.Gauge: DrawGauge(g, pen); break;
                    case IconId.Bolt: DrawBolt(g, pen); break;
                    case IconId.Wave: DrawWave(g, pen); break;
                    case IconId.Fan: DrawFan(g, pen); break;
                    case IconId.Pump: DrawPump(g, pen); break;
                    case IconId.Up: DrawArrow(g, pen, up: true, tray: false); break;
                    case IconId.Down: DrawArrow(g, pen, up: false, tray: false); break;
                    case IconId.DiskRead: DrawArrow(g, pen, up: true, tray: true); break;
                    case IconId.DiskWrite: DrawArrow(g, pen, up: false, tray: true); break;
                    case IconId.Battery: DrawBattery(g, pen, fill, fillPct, accent); break;
                    case IconId.Clock: DrawClock(g, pen); break;
                    case IconId.Stopwatch: DrawStopwatch(g, pen); break;
                    case IconId.Globe: DrawGlobe(g, pen); break;
                    case IconId.Display: DrawDisplay(g, pen); break;
                    case IconId.Gamepad: DrawGamepad(g, pen, fill); break;
                    default: DrawPulse(g, pen); break;
                }
            }
            finally
            {
                g.Transform = old;
                g.SmoothingMode = oldSmooth;
            }
        }

        private static Pen CreatePen(Color color, float widthUnits, float scale)
        {
            // 设备像素描边 = widthUnits * scale，最低 1px 保证小图标可读
            float dev = MathF.Max(1f, widthUnits * scale);
            var p = new Pen(color, dev / MathF.Max(scale, 0.01f));
            p.StartCap = LineCap.Round;
            p.EndCap = LineCap.Round;
            p.LineJoin = LineJoin.Round;
            return p;
        }

        private static GraphicsPath RoundedPath(float x, float y, float w, float h, float r)
        {
            var path = new GraphicsPath();
            float d = r * 2;
            if (d <= 0f || w < d || h < d)
            {
                path.AddRectangle(new RectangleF(x, y, w, h));
                return path;
            }
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // =========================================================
        // 各图标几何（网格坐标）
        // =========================================================
        private static void DrawChip(Graphics g, Pen p, SolidBrush fill)
        {
            using var outer = RoundedPath(5f, 5f, 14f, 14f, 2.6f);
            g.DrawPath(p, outer);
            using var inner = RoundedPath(9.6f, 9.6f, 4.8f, 4.8f, 1f);
            g.DrawPath(p, inner);

            foreach (float o in new[] { 9f, 15f })
            {
                g.DrawLine(p, o, 1.8f, o, 5f);           // 上
                g.DrawLine(p, o, 19f, o, 22.2f);         // 下
                g.DrawLine(p, 1.8f, o, 5f, o);           // 左
                g.DrawLine(p, 19f, o, 22.2f, o);         // 右
            }
        }

        private static void DrawGpu(Graphics g, Pen p, SolidBrush fill)
        {
            using var body = RoundedPath(2.4f, 5.6f, 17.2f, 12.6f, 2.4f);
            g.DrawPath(p, body);

            // 风扇（外圈 + 轮毂）
            g.DrawEllipse(p, 9.8f, 8.6f, 6.8f, 6.8f);
            g.FillEllipse(fill, 12.5f, 11.3f, 1.4f, 1.4f);

            // 挡板：左侧竖线 + 底部支脚
            g.DrawLine(p, 5.6f, 9.4f, 5.6f, 14.4f);
            g.DrawLine(p, 4.2f, 18.2f, 4.2f, 21.2f);
            g.DrawLine(p, 4.2f, 21.2f, 8.2f, 21.2f);
        }

        private static void DrawDimm(Graphics g, Pen p)
        {
            using var body = RoundedPath(2.4f, 8f, 19.2f, 6.8f, 1.8f);
            g.DrawPath(p, body);

            g.DrawLine(p, 9.2f, 10.2f, 9.2f, 12.6f);     // 颗粒
            g.DrawLine(p, 14.8f, 10.2f, 14.8f, 12.6f);

            foreach (float x in new[] { 5.8f, 9.8f, 14.2f, 18.2f })
                g.DrawLine(p, x, 15.6f, x, 18.4f);       // 金手指
        }

        private static void DrawVram(Graphics g, Pen p)
        {
            using var body = RoundedPath(5.4f, 6.6f, 13.2f, 10.8f, 2f);
            g.DrawPath(p, body);

            foreach (float y in new[] { 9.2f, 12f, 14.8f })
            {
                g.DrawLine(p, 2.2f, y, 5.4f, y);         // 左引脚
                g.DrawLine(p, 18.6f, y, 21.8f, y);       // 右引脚
            }
        }

        private static void DrawThermo(Graphics g, Pen p)
        {
            g.DrawArc(p, 9.7f, 4.2f, 4.6f, 4.6f, 180, 180); // 顶部圆头
            g.DrawLine(p, 9.7f, 6.5f, 9.7f, 13.2f);
            g.DrawLine(p, 14.3f, 6.5f, 14.3f, 13.2f);
            g.DrawEllipse(p, 8.3f, 12.9f, 7.4f, 7.4f);      // 玻泡
            g.DrawLine(p, 12f, 13.2f, 12f, 8.2f);           // 水银柱
        }

        private static void DrawGauge(Graphics g, Pen p)
        {
            g.DrawArc(p, 4f, 5.5f, 16f, 16f, 150, 240);     // 240° 表盘
            g.DrawLine(p, 12f, 13.5f, 16f, 9.5f);           // 指针
        }

        private static void DrawBolt(Graphics g, Pen p)
        {
            using var path = new GraphicsPath();
            path.AddLines(new[]
            {
                new PointF(13.4f, 2.2f), new PointF(4.2f, 13.8f), new PointF(11.4f, 13.8f),
                new PointF(10.6f, 21.8f), new PointF(19.8f, 9.6f), new PointF(12.6f, 9.6f),
                new PointF(13.4f, 2.2f)
            });
            g.DrawPath(p, path);
        }

        private static void DrawWave(Graphics g, Pen p)
        {
            using var path = new GraphicsPath();
            path.AddBezier(2.8f, 12f, 5.4f, 5f, 8.6f, 5f, 12f, 12f);
            path.AddBezier(12f, 12f, 15.4f, 19f, 18.6f, 19f, 21.2f, 12f);
            g.DrawPath(p, path);
        }

        private static void DrawFan(Graphics g, Pen p)
        {
            g.FillEllipse(p.Brush, 10.6f, 10.6f, 2.8f, 2.8f); // 轮毂

            // 三片弯刀叶（旋转对称的环扇段）
            for (int i = 0; i < 3; i++)
            {
                float a0 = -78f + i * 120f;
                float a1 = a0 + 78f;
                double rad1 = a1 * Math.PI / 180.0, rad2 = (a0 + 108) * Math.PI / 180.0;

                var path = new GraphicsPath();
                // 外缘弧：θ0 → θ0+78
                path.AddArc(3.4f, 3.4f, 17.2f, 17.2f, a0, 78f);
                // 径向切到内缘
                float ox = 12f + 8.6f * (float)Math.Cos(rad1), oy = 12f + 8.6f * (float)Math.Sin(rad1);
                float ix = 12f + 3.1f * (float)Math.Cos(rad1), iy = 12f + 3.1f * (float)Math.Sin(rad1);
                path.AddLine(ox, oy, ix, iy);
                // 内缘弧（带 30° 前掠角）
                path.AddArc(8.9f, 8.9f, 6.2f, 6.2f, a0 + 108f, 78f);
                path.CloseFigure();
                g.DrawPath(p, path);
            }
        }

        private static void DrawPump(Graphics g, Pen p)
        {
            g.DrawEllipse(p, 3.6f, 3.6f, 16.8f, 16.8f);

            // 水滴：窄顶尖 + 圆底
            using var drop = new GraphicsPath();
            drop.AddBezier(12f, 7.4f, 10.9f, 9.4f, 9.7f, 10.9f, 9.7f, 12.8f);
            drop.AddArc(9.7f, 10.2f, 4.6f, 4.6f, 180f, 180f);
            drop.AddBezier(14.3f, 12.8f, 14.3f, 10.9f, 13.1f, 9.4f, 12f, 7.4f);
            drop.CloseFigure();
            g.DrawPath(p, drop);
        }

        private static void DrawArrow(Graphics g, Pen p, bool up, bool tray)
        {
            // 托盘（磁盘读/写）：三面开口托盘
            if (tray)
            {
                g.DrawLine(p, 4.8f, 15.4f, 4.8f, 19.6f);
                g.DrawLine(p, 4.8f, 19.6f, 19.2f, 19.6f);
                g.DrawLine(p, 19.2f, 19.6f, 19.2f, 15.4f);
            }

            // 箭杆：向上的箭头在托盘模式下收短
            float yTail = up ? (tray ? 15.8f : 19.6f) : (tray ? 16.2f : 19.6f);
            g.DrawLine(p, 12f, 4.6f, 12f, yTail);

            // 箭头（方向翻转）
            float yTip = up ? 4.6f : 19.6f;
            float yWing = up ? 9.4f : 14.8f;
            g.DrawLine(p, 7.4f, yWing, 12f, yTip);
            g.DrawLine(p, 16.6f, yWing, 12f, yTip);
        }

        private static void DrawBattery(Graphics g, Pen p, SolidBrush fill, float pct, bool charging)
        {
            using var shell = RoundedPath(2.6f, 7.9f, 17.4f, 8.6f, 2.5f);
            g.DrawPath(p, shell);
            g.DrawLine(p, 21.5f, 10.7f, 21.5f, 13.7f);      // 正极凸头

            if (charging)
            {
                // 闪电（充电中）
                using var bolt = new GraphicsPath();
                bolt.AddLines(new[]
                {
                    new PointF(12.4f, 6.8f), new PointF(8.6f, 12.6f), new PointF(11.5f, 12.6f),
                    new PointF(10.9f, 17.6f), new PointF(15.4f, 11.5f), new PointF(12.4f, 11.5f),
                    new PointF(12.4f, 6.8f)
                });
                g.DrawPath(p, bolt);
            }
            else if (pct >= 0f)
            {
                float w = MathF.Max(1.6f, 12.2f * Math.Clamp(pct, 0f, 1f));
                using var bar = RoundedPath(5f, 10.3f, w, 3.8f, 1.1f);
                g.FillPath(fill, bar);
            }
        }

        private static void DrawClock(Graphics g, Pen p)
        {
            g.DrawEllipse(p, 3.6f, 3.6f, 16.8f, 16.8f);
            g.DrawLine(p, 12f, 7f, 12f, 12.2f);
            g.DrawLine(p, 12f, 12.2f, 15.4f, 13.9f);
        }

        private static void DrawStopwatch(Graphics g, Pen p)
        {
            g.DrawEllipse(p, 5f, 6.6f, 14f, 14f);
            g.DrawLine(p, 12f, 2.8f, 12f, 6.4f);            // 顶部按钮
            g.DrawLine(p, 12f, 13.6f, 15f, 10.8f);          // 指针
        }

        private static void DrawGlobe(Graphics g, Pen p)
        {
            g.DrawEllipse(p, 3.6f, 3.6f, 16.8f, 16.8f);
            g.DrawLine(p, 3.6f, 12f, 20.4f, 12f);           // 赤道
            g.DrawEllipse(p, 8.2f, 3.6f, 7.6f, 16.8f);      // 经线
        }

        private static void DrawDisplay(Graphics g, Pen p)
        {
            using var screen = RoundedPath(2.8f, 4.4f, 18.4f, 12.4f, 2.2f);
            g.DrawPath(p, screen);
            g.DrawLine(p, 12f, 16.8f, 12f, 20.2f);          // 支架
            g.DrawLine(p, 8.4f, 20.4f, 15.6f, 20.4f);       // 底座
        }

        private static void DrawGamepad(Graphics g, Pen p, SolidBrush fill)
        {
            // FPS 准星：圆环 + 四向刻线 + 中心点（小尺寸下手柄不可读）
            g.DrawEllipse(p, 6.9f, 6.9f, 10.2f, 10.2f);
            g.DrawLine(p, 12f, 2.6f, 12f, 6.6f);
            g.DrawLine(p, 12f, 17.4f, 12f, 21.4f);
            g.DrawLine(p, 2.6f, 12f, 6.6f, 12f);
            g.DrawLine(p, 17.4f, 12f, 21.4f, 12f);
            g.FillEllipse(fill, 10.9f, 10.9f, 2.2f, 2.2f);
        }

        private static void DrawPulse(Graphics g, Pen p)
        {
            using var path = new GraphicsPath();
            path.AddLines(new[]
            {
                new PointF(2.6f, 12.6f), new PointF(7.2f, 12.6f), new PointF(9.7f, 6.2f),
                new PointF(13.7f, 17.8f), new PointF(16.2f, 12.6f), new PointF(21.4f, 12.6f)
            });
            g.DrawPath(p, path);
        }
    }
}
