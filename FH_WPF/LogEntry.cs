using System;
using System.Windows.Media;

namespace FH_WPF
{
    /// <summary>
    /// 日志条目，包含时间、级别、模块和内容四个字段。
    /// </summary>
    internal class LogEntry
    {
        public string Time { get; }
        public string Level { get; }
        public string Module { get; }
        public string Content { get; }

        /// <summary>级别标签背景色（信息=蓝，成功=绿，警告=橙，错误=红）</summary>
        public Brush LevelBackground { get; }

        public LogEntry(string level, string module, string content)
        {
            Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Level = level;
            Module = module;
            Content = content;
            LevelBackground = ResolveLevelBrush(level);
        }

        private static Brush ResolveLevelBrush(string level) => level switch
        {
            "成功" => new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),
            "警告" => new SolidColorBrush(Color.FromRgb(0xE6, 0x5C, 0x00)),
            "错误" => new SolidColorBrush(Color.FromRgb(0xB7, 0x1C, 0x1C)),
            _      => new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)), // 信息
        };
    }
}
