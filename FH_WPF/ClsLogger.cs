using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace FH_WPF
{
    internal static class ClsLogger
    {
        /// <summary>日志条目集合，绑定到 UI ListView</summary>
        public static ObservableCollection<LogEntry> Entries { get; } = new ObservableCollection<LogEntry>();

        private static Dispatcher? _dispatcher;

        // 日志目录
        private static string _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        // 文件写入锁
        private static readonly object _fileLock = new object();

        // 解析消息开头的 [级别] 标记，例如 "[信息] 脚本已启动"
        private static readonly Regex _levelRegex = new Regex(@"^\[([^\]]+)\]\s*", RegexOptions.Compiled);

        /// <summary>
        /// 初始化 logger，传入 UI Dispatcher 用于跨线程更新，可选指定日志目录。
        /// 旧签名保留以向后兼容（TextBox 参数被忽略）。
        /// </summary>
        public static void Init(object? ignored1 = null, object? ignored2 = null, object? ignored3 = null, string? logDirectory = null)
        {
            _dispatcher = System.Windows.Application.Current?.Dispatcher;

            if (!string.IsNullOrWhiteSpace(logDirectory))
                _logDirectory = logDirectory!;

            try
            {
                if (!Directory.Exists(_logDirectory))
                    Directory.CreateDirectory(_logDirectory);
            }
            catch { }
        }

        /// <summary>默认写全局日志（兼容旧调用）</summary>
        public static void Log(string message) => LogWithModule(message, "系统");

        public static void LogGlobal(string message) => LogWithModule(message, "系统");

        public static void LogScript(string message) => LogWithModule(message, "脚本");

        public static void LogPoint(string message) => LogWithModule(message, "熟练度");

        /// <summary>
        /// 带明确级别和模块的结构化日志写入。
        /// </summary>
        public static void LogStructured(string level, string module, string content)
        {
            content ??= string.Empty;
            var now = DateTime.Now;
            WriteToFile(now, level, module, content);
            AppendEntry(new LogEntry(level, module, content));
        }

        /// <summary>写异常日志，包含堆栈信息</summary>
        public static void LogException(Exception ex, string? note = null)
        {
            if (ex == null) return;
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(note)) sb.AppendLine(note);
            sb.AppendLine(ex.ToString());
            LogWithModule(sb.ToString(), "系统");
        }

        // ── 私有方法 ────────────────────────────────────────────

        /// <summary>从消息里解析 [级别] 前缀，剩余部分为内容</summary>
        private static void LogWithModule(string message, string module)
        {
            message ??= string.Empty;
            var m = _levelRegex.Match(message);
            string level;
            string content;
            if (m.Success)
            {
                level = MapLevel(m.Groups[1].Value);
                content = message[m.Length..].Trim();
            }
            else
            {
                level = "信息";
                content = message.Trim();
            }
            LogStructured(level, module, content);
        }

        /// <summary>将各种前缀文字统一为标准级别</summary>
        private static string MapLevel(string raw) => raw switch
        {
            "成功" or "完成" => "成功",
            "警告" or "注意" => "警告",
            "错误" or "异常" or "失败" => "错误",
            _ => "信息",
        };

        private static void WriteToFile(DateTime now, string level, string module, string content)
        {
            try
            {
                var line = $"[{now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [{module}] {content}";
                var filePath = Path.Combine(_logDirectory, now.ToString("yyyy-MM-dd") + ".log");
                lock (_fileLock)
                {
                    var dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    using var sw = new StreamWriter(filePath, true, Encoding.UTF8);
                    sw.WriteLine(line);
                }
            }
            catch { }
        }

        private static void AppendEntry(LogEntry entry)
        {
            try
            {
                var disp = _dispatcher ?? System.Windows.Application.Current?.Dispatcher;
                if (disp == null) return;

                if (disp.CheckAccess())
                    Entries.Add(entry);
                else
                    disp.Invoke(() => Entries.Add(entry), DispatcherPriority.Input);
            }
            catch { }
        }
    }
}
