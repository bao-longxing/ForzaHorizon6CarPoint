using System;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace FH_WPF
{
    internal static class ClsMousePos
    {
        // Public control
        public static bool IsRunning => _timer != null && _timer.IsEnabled;

        public static void Start()
        {
            if (IsRunning) return;

            var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            dispatcher.Invoke(() =>
            {
                if (_overlay == null)
                {
                    _overlay = new OverlayWindow();
                }

                if (_timer == null)
                {
                    _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
                    {
                        Interval = TimeSpan.FromMilliseconds(50)
                    };
                    _timer.Tick += Timer_Tick;
                }

                _overlay.Show();
                _timer.Start();
            });
        }

        public static void Stop()
        {
            if (!IsRunning && _overlay == null) return;

            var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            dispatcher.Invoke(() =>
            {
                try
                {
                    _timer?.Stop();
                    if (_overlay != null)
                    {
                        _overlay.Close();
                        _overlay = null;
                    }
                }
                catch { }
                finally
                {
                    if (_timer != null)
                    {
                        _timer.Tick -= Timer_Tick;
                        _timer = null;
                    }
                }
            });
        }

        public static void Toggle()
        {
            if (IsRunning) Stop(); else Start();
        }

        // Private implementation
        private static DispatcherTimer _timer;
        private static OverlayWindow _overlay;

        private static void Timer_Tick(object sender, EventArgs e)
        {
            if (_overlay == null) return;
            var p = GetCursorPosition();
            _overlay.UpdateText($"X: {p.X}  Y: {p.Y}");
        }

        // Get global cursor position via user32 GetCursorPos loaded dynamically (no DllImport extern)
        private delegate bool GetCursorPosDelegate(out POINT pt);

        private static GetCursorPosDelegate? _getCursorPos;
        private static readonly object _nativeInitLock = new object();

        private static (int X, int Y) GetCursorPosition()
        {
            try
            {
                EnsureGetCursorPos();
                if (_getCursorPos != null && _getCursorPos.Invoke(out var pt))
                {
                    return (pt.X, pt.Y);
                }
            }
            catch { }
            return (0, 0);
        }

        private static void EnsureGetCursorPos()
        {
            if (_getCursorPos != null) return;
            lock (_nativeInitLock)
            {
                if (_getCursorPos != null) return;
                try
                {
                    IntPtr lib = System.Runtime.InteropServices.NativeLibrary.Load("user32.dll");
                    IntPtr proc = System.Runtime.InteropServices.NativeLibrary.GetExport(lib, "GetCursorPos");
                    _getCursorPos = Marshal.GetDelegateForFunctionPointer<GetCursorPosDelegate>(proc);
                    // Note: we intentionally do not free the library handle to keep delegate valid
                }
                catch
                {
                    _getCursorPos = null;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        // Overlay window implementation
        private class OverlayWindow : Window
        {
            private readonly TextBlock _text;

            public OverlayWindow()
            {
                WindowStyle = WindowStyle.None;
                AllowsTransparency = true;
                Background = System.Windows.Media.Brushes.Transparent;
                ShowInTaskbar = false;
                Topmost = true;
                Width = 240;
                Height = 30;
                ResizeMode = ResizeMode.NoResize;
                // Make the window not focusable and ignore mouse
                Focusable = false;

                var grid = new Grid { Background = System.Windows.Media.Brushes.Transparent, IsHitTestVisible = false };

                var border = new Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(160, 0, 0, 0)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    VerticalAlignment = System.Windows.VerticalAlignment.Top,
                    IsHitTestVisible = false
                };

                _text = new TextBlock
                {
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 14,
                    Text = "X: 0  Y: 0",
                    IsHitTestVisible = false
                };

                border.Child = _text;
                grid.Children.Add(border);
                Content = grid;

                Loaded += OverlayWindow_Loaded;
            }

            private void OverlayWindow_Loaded(object sender, RoutedEventArgs e)
            {
                // Position at right-top of primary screen with small margin
                const double margin = 8;
                Left = SystemParameters.PrimaryScreenWidth - Width - margin;
                Top = margin;
                // Don't activate when showing and don't receive hit tests
                ShowActivated = false;
                IsHitTestVisible = false;
            }

            public void UpdateText(string s)
            {
                _text.Text = s;
            }

            // No native interop here to keep hot-reload friendly
        }
    }
}
