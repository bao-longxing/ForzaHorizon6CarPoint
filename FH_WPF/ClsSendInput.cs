using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Input;

namespace FH_WPF
{
    internal static class ClsSendInput
    {
        // --------------------------------------------------------------------------------
        // Windows API 定义
        // --------------------------------------------------------------------------------
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        // 常量定义
        private const uint INPUT_MOUSE = 0;
        private const uint INPUT_KEYBOARD = 1;

        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_XDOWN = 0x0080;
        private const uint MOUSEEVENTF_XUP = 0x0100;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        private const uint KEYEVENTF_KEYUP = 0x0002;

        // --------------------------------------------------------------------------------
        // 辅助逻辑
        // --------------------------------------------------------------------------------
        private static int GetRandomizedDelay(int baseMs)
        {
            if (baseMs <= 0) return 0;
            return Random.Shared.Next(Math.Max(1, baseMs / 2), baseMs * 2 + 1);
        }

        // 将 WPF Key 转换为 Windows 虚拟键码 (VK)
        private static ushort ToVk(Key key) => (ushort)KeyInterop.VirtualKeyFromKey(key);

        // --------------------------------------------------------------------------------
        // 鼠标核心方法
        // --------------------------------------------------------------------------------

        /// <summary>
        /// 1=左键, 2=中键, 3=右键, 4=侧键后(XBUTTON1), 5=侧键前(XBUTTON2)
        /// </summary>
        public static void MouseDown(int button) => SendMouseInput(button, true);
        public static void MouseUp(int button) => SendMouseInput(button, false);

        public static void ClickMouse(int button, int holdMs = 100)
        {
            MouseDown(button);
            Thread.Sleep(GetRandomizedDelay(holdMs));
            MouseUp(button);
        }

        private static void SendMouseInput(int button, bool isDown)
        {
            INPUT input = new INPUT { type = INPUT_MOUSE };
            uint flag = 0;

            switch (button)
            {
                case 1: flag = isDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP; break;
                case 2: flag = isDown ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP; break;
                case 3: flag = isDown ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP; break;
                case 4:
                    flag = isDown ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP;
                    input.U.mi.mouseData = 1; // XBUTTON1
                    break;
                case 5:
                    flag = isDown ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP;
                    input.U.mi.mouseData = 2; // XBUTTON2
                    break;
            }

            input.U.mi.dwFlags = flag;
            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        /// <summary>
        /// 鼠标移动。absMove=true 为绝对坐标，false 为相对当前位置移动。
        /// </summary>
        public static void Move(int x, int y, bool absMove = false)
        {
            INPUT input = new INPUT { type = INPUT_MOUSE };

            if (absMove)
            {
                // Windows SendInput 绝对坐标系是 0-65535，这里采用更省心的基于当前坐标计算差值
                if (GetCursorPos(out POINT currentPos))
                {
                    input.U.mi.dx = x - currentPos.X;
                    input.U.mi.dy = y - currentPos.Y;
                    input.U.mi.dwFlags = MOUSEEVENTF_MOVE;
                }
            }
            else
            {
                input.U.mi.dx = x;
                input.U.mi.dy = y;
                input.U.mi.dwFlags = MOUSEEVENTF_MOVE;
            }

            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        // --------------------------------------------------------------------------------
        // 键盘核心方法
        // --------------------------------------------------------------------------------
        public static void KeyDown(Key key) => SendKeyInput(ToVk(key), true);
        public static void KeyUp(Key key) => SendKeyInput(ToVk(key), false);

        public static void ClickKey(Key key, int holdMs = 50)
        {
            KeyDown(key);
            Thread.Sleep(GetRandomizedDelay(holdMs));
            KeyUp(key);
        }

        private static void SendKeyInput(ushort vk, bool isDown)
        {
            INPUT input = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vk,
                        dwFlags = isDown ? 0 : KEYEVENTF_KEYUP
                    }
                }
            };
            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        // 支持字符串直接调用的重载
        public static void KeyDown(string keyName) => KeyDown(Enum.Parse<Key>(keyName, true));
        public static void KeyUp(string keyName) => KeyUp(Enum.Parse<Key>(keyName, true));
        public static void ClickKey(string keyName, int holdMs = 50) => ClickKey(Enum.Parse<Key>(keyName, true), holdMs);

        // --------------------------------------------------------------------------------
        // 文本输入（大幅度简化版，支持大小写、数字及空格）
        // --------------------------------------------------------------------------------
        public static void InputText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            foreach (var ch in text)
            {
                if (ch == ' ')
                {
                    ClickKey(Key.Space, 30);
                }
                else if (char.IsLetter(ch))
                {
                    bool isUpper = char.IsUpper(ch);
                    Key key = Enum.Parse<Key>(ch.ToString(), true);

                    if (isUpper) KeyDown(Key.LeftShift);
                    ClickKey(key, 30);
                    if (isUpper) KeyUp(Key.LeftShift);
                }
                else if (char.IsDigit(ch))
                {
                    Key key = Enum.Parse<Key>("D" + ch, true);
                    ClickKey(key, 30);
                }
                // 如果需要支持高阶特殊符号(如 !, @, #) 可以根据需要补充映射，此处保留基础骨架
                Thread.Sleep(10);
            }
        }
    }
}