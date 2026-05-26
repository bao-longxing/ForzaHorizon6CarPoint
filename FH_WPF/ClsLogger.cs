using System;
using System.IO;
using System.Text;
using System.Windows.Controls;

namespace FH_WPF
{
    internal static class ClsLogger
    {
        // 目标 TextBox（可为 null）
        private static TextBox? _globalTextBox;
        private static TextBox? _scriptTextBox;
        private static TextBox? _pointTextBox;

        // 日志目录
        private static string _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        // 文件写入锁
        private static readonly object _fileLock = new object();

        /// <summary>
        /// 初始化 logger，传入三个日志 TextBox，可选指定日志目录
        /// </summary>
        public static void Init(TextBox? globalTextBox, TextBox? scriptTextBox = null, TextBox? pointTextBox = null, string? logDirectory = null)
        {
            _globalTextBox = globalTextBox;
            _scriptTextBox = scriptTextBox;
            _pointTextBox = pointTextBox;

            if (!string.IsNullOrWhiteSpace(logDirectory))
            {
                _logDirectory = logDirectory!;
            }

            try
            {
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }
            }
            catch
            {
                // 忽略目录创建错误，写文件时会再次尝试
            }
        }

        /// <summary>
        /// 默认写全局日志（兼容旧调用）
        /// </summary>
        public static void Log(string message)
        {
            LogGlobal(message);
        }

        public static void LogGlobal(string message)
        {
            LogInternal(message, "全局", _globalTextBox);
        }

        public static void LogScript(string message)
        {
            LogInternal(message, "脚本", _scriptTextBox);
        }

        public static void LogPoint(string message)
        {
            LogInternal(message, "点数", _pointTextBox);
        }

        private static void LogInternal(string message, string category, TextBox? targetTextBox)
        {
            message ??= string.Empty;
            var now = DateTime.Now;
            var fileLine = $"[{now:yyyy-MM-dd HH:mm:ss.fff}] [{category}] {message}";
            var uiLine = $"[{now:HH:mm:ss}] [{category}] {message}";

            // 写入文件（按日期分文件）
            try
            {
                var filePath = Path.Combine(_logDirectory, now.ToString("yyyy-MM-dd") + ".log");
                lock (_fileLock)
                {
                    var dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    using (var sw = new StreamWriter(filePath, true, Encoding.UTF8))
                    {
                        sw.WriteLine(fileLine);
                    }
                }
            }
            catch
            {
                // 忽略文件写入错误，保证不会抛到调用方
            }

            // 更新 TextBox（如果有）
            try
            {
                if (targetTextBox != null)
                {
                    var disp = targetTextBox.Dispatcher;
                    if (disp != null)
                    {
                        // 如果当前线程是 UI 线程，直接更新；否则使用同步的 Invoke 保证在返回前完成更新，避免日志丢失
                        try
                        {
                            if (disp.CheckAccess())
                            {
                                if (targetTextBox.Text.Length > 0)
                                    targetTextBox.AppendText(Environment.NewLine + uiLine);
                                else
                                    targetTextBox.AppendText(uiLine);

                                targetTextBox.ScrollToEnd();
                            }
                            else
                            {
                                disp.Invoke(new Action(() =>
                                {
                                    try
                                    {
                                        if (targetTextBox.Text.Length > 0)
                                            targetTextBox.AppendText(Environment.NewLine + uiLine);
                                        else
                                            targetTextBox.AppendText(uiLine);

                                        targetTextBox.ScrollToEnd();
                                    }
                                    catch { }
                                }), System.Windows.Threading.DispatcherPriority.Input);
                            }
                        }
                        catch (Exception)
                        {
                            // 如果更新目标 TextBox 失败，尝试回退写入全局日志
                            try
                            {
                                if (_globalTextBox != null)
                                {
                                    var gdisp = _globalTextBox.Dispatcher;
                                    if (gdisp != null)
                                    {
                                        if (gdisp.CheckAccess())
                                        {
                                            _globalTextBox.AppendText(Environment.NewLine + uiLine);
                                            _globalTextBox.ScrollToEnd();
                                        }
                                        else
                                        {
                                            gdisp.Invoke(new Action(() =>
                                            {
                                                try
                                                {
                                                    _globalTextBox.AppendText(Environment.NewLine + uiLine);
                                                    _globalTextBox.ScrollToEnd();
                                                }
                                                catch { }
                                            }), System.Windows.Threading.DispatcherPriority.Input);
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        // Dispatcher 不可用时回退到全局日志（如果存在）
                        try
                        {
                            if (_globalTextBox != null)
                            {
                                var gdisp = _globalTextBox.Dispatcher;
                                if (gdisp != null)
                                {
                                    if (gdisp.CheckAccess())
                                    {
                                        _globalTextBox.AppendText(Environment.NewLine + uiLine);
                                        _globalTextBox.ScrollToEnd();
                                    }
                                    else
                                    {
                                        gdisp.Invoke(new Action(() =>
                                        {
                                            try
                                            {
                                                _globalTextBox.AppendText(Environment.NewLine + uiLine);
                                                _globalTextBox.ScrollToEnd();
                                            }
                                            catch { }
                                        }), System.Windows.Threading.DispatcherPriority.Input);
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch
            {
                // 忽略 dispatcher 调用错误
            }
        }

        /// <summary>
        /// 写异常日志，包含堆栈信息
        /// </summary>
        public static void LogException(Exception ex, string? note = null)
        {
            if (ex == null) return;
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(note)) sb.AppendLine(note);
            sb.AppendLine(ex.ToString());
            LogGlobal(sb.ToString());
        }
    }
}
