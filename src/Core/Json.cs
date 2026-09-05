using System.Text.Json;

namespace LiteMonitor.src.Core
{
    /// <summary>
    /// JSON 序列化统一入口：集中缓存 JsonSerializerOptions（避免每次调用重建），
    /// 并提供 JSON 深拷贝工具（此前在 SettingsChanger/MonitorPage/Settings 等处复制粘贴了 6+ 份）。
    /// </summary>
    public static class Json
    {
        /// <summary>紧凑格式（配置回写、深拷贝、网络传输通用）</summary>
        public static readonly JsonSerializerOptions Compact = new JsonSerializerOptions
        {
            WriteIndented = false
        };

        /// <summary>缩进格式（settings.json 等需要人可读的落盘文件）</summary>
        public static readonly JsonSerializerOptions Indented = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        /// <summary>反序列化用户手写/旧版本 JSON 时的宽松匹配</summary>
        public static readonly JsonSerializerOptions CaseInsensitive = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>主题文件解析：容忍尾随逗号 + 忽略只读属性（原 ThemeManager/ThemeFileService 各持一份）</summary>
        public static readonly JsonSerializerOptions Theme = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            IgnoreReadOnlyProperties = true,
            AllowTrailingCommas = true
        };

        /// <summary>
        /// 通过 JSON 往返实现深拷贝（简单对象图专用；不适用含循环引用/多态的场景）
        /// </summary>
        public static T? Clone<T>(T obj)
        {
            return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(obj, Compact), Compact);
        }
    }
}
