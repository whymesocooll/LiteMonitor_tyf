using System.Diagnostics;
using System.Text;

namespace LiteMonitor.src.Core
{
    /// <summary>
    /// 轻量日志门面：所有 catch/关键路径的异常都应经由这里落盘，避免静默失败。
    /// 文件写入程序目录 LiteMonitor_Log.log（与 LiteMonitor_Error.log 分开，只记诊断不弹窗）。
    /// </summary>
    public static class Log
    {
        private static readonly object _lock = new();
        private static readonly string _logPath = Path.Combine(AppContext.BaseDirectory, "LiteMonitor_Log.log");

        private const long MaxLogBytes = 2 * 1024 * 1024; // 超过 2MB 轮转为 .old

        public static void Debug(string message) => Write("DEBUG", message, null, fileEnabled: Debugger.IsAttached);

        public static void Info(string message) => Write("INFO", message, null, fileEnabled: true);

        public static void Warn(string message) => Write("WARN", message, null, fileEnabled: true);

        public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex, fileEnabled: true);

        private static void Write(string level, string message, Exception? ex, bool fileEnabled)
        {
            var sb = new StringBuilder(256);
            sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("][")
              .Append(level).Append("][")
              .Append(Environment.CurrentManagedThreadId).Append("] ")
              .Append(message);
            if (ex != null)
            {
                sb.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
                if (ex.StackTrace is { } stack) sb.Append('\n').Append(stack);
            }
            string line = sb.ToString();

            System.Diagnostics.Debug.WriteLine("LiteMonitor." + line);
            if (!fileEnabled) return;

            try
            {
                lock (_lock)
                {
                    RotateIfNeeded();
                    File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // 日志写不进去（磁盘满/权限受限）时只能放弃，不能再抛
            }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                if (!File.Exists(_logPath)) return;
                var fi = new FileInfo(_logPath);
                if (fi.Length < MaxLogBytes) return;

                string oldPath = _logPath + ".old";
                if (File.Exists(oldPath)) File.Delete(oldPath);
                File.Move(_logPath, oldPath);
            }
            catch
            {
                // 轮转失败不影响本次写入尝试
            }
        }
    }
}
