using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace FH_WPF
{
    /// <summary>
    /// 提供一个全局的低级键盘钩子，用于捕获 F1-F12 功能键并通过事件通知外部。
    /// 订阅者可以通过设置 FunctionKeyEventArgs.Handled 来决定是否拦截该按键。
    /// </summary>
    internal sealed class ClsKeyboardHook : IDisposable
    {
        #region 常量与字段
        // 低级键盘钩子常量
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;
        private bool _disposed;
        #endregion

        #region 构造与析构
        /// <summary>
        /// 构造函数，准备钩子回调代理
        /// </summary>
        public ClsKeyboardHook()
        {
            _proc = HookCallback;
        }

        ~ClsKeyboardHook()
        {
            Dispose(false);
        }
        #endregion

        #region 事件
        /// <summary>
        /// 当捕获到 F1-F12 功能键按下时触发。
        /// 订阅者可通过设置 e.Handled = true 来阻止该按键继续传递给其他应用/窗口。
        /// </summary>
        public event EventHandler<FunctionKeyEventArgs>? FunctionKeyPressed;
        #endregion

        #region 公共方法
        /// <summary>
        /// 启动钩子（若已启动则忽略）。
        /// </summary>
        public void Start()
        {
            if (_disposed || _hookId != IntPtr.Zero)
            {
                return;
            }

            _hookId = SetHook(_proc);
        }

        /// <summary>
        /// 停止钩子（若未启动则忽略）。
        /// </summary>
        public void Stop()
        {
            if (_hookId == IntPtr.Zero)
            {
                return;
            }

            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        #endregion

        #region 钩子实现
        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            // 将钩子安装到当前进程的模块上
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule!)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                // 只处理按下消息（包括系统键）
                if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
                {
                    KBDLLHOOKSTRUCT kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    int vk = kb.vkCode;

                    // F1..F12 虚拟键码范围 0x70 - 0x7B (112 - 123)
                    if (vk >= 0x70 && vk <= 0x7B)
                    {
                        Key key = KeyInterop.KeyFromVirtualKey(vk);
                        var args = new FunctionKeyEventArgs(key);

                        // 触发事件给订阅者
                        EventHandler<FunctionKeyEventArgs>? handler = FunctionKeyPressed;
                        handler?.Invoke(this, args);

                        // 如果订阅者将 Handled 设为 true，则阻止继续传递
                        if (args.Handled)
                        {
                            return (IntPtr)1; // 非零表示已处理
                        }
                    }
                }
            }
            catch
            {
                // 回调中捕获异常应静默处理，避免破坏钩子链
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
        #endregion

        #region P/Invoke
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        #endregion

        #region IDisposable
        /// <summary>
        /// 释放并移除已安装的钩子。
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _disposed = true;
        }
        #endregion
    }

    #region FunctionKeyEventArgs
    /// <summary>
    /// 功能键事件参数，包含被按下的 Key 和是否拦截该按键的标志。
    /// </summary>
    internal sealed class FunctionKeyEventArgs : EventArgs
    {
        /// <summary>
        /// 创建新的事件参数实例。
        /// </summary>
        public FunctionKeyEventArgs(Key key)
        {
            Key = key;
            Handled = false;
        }

        /// <summary>
        /// 被按下的功能键（F1-F12）。
        /// </summary>
        public Key Key { get; private set; }

        /// <summary>
        /// 如果为 true，表示订阅者已处理该按键并希望拦截它，阻止继续分发。
        /// </summary>
        public bool Handled { get; set; }
    }
    #endregion
}
