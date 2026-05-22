using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace FH_WPF
{
    internal static class ClsLogicContorl_Ghub 
    {
        // 最近一次错误信息（诊断用）
        private static string _lastError = string.Empty;
        public static string LastError => _lastError;
        // 用于生成随机化的按键/鼠标按下时长，使用 Random.Shared（线程安全）
        private static int GetRandomizedDelay(int baseMs)
        {
            if (baseMs <= 0) return 0;
            int min = Math.Max(1, baseMs / 2);
            int max = Math.Max(min + 1, baseMs * 2);
            return Random.Shared.Next(min, max + 1);
        }
        private const string DllName = "ghub_device.dll";
        public static bool IsInitialized { get; private set; }

        static ClsLogicContorl_Ghub()
        {
            var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Environment.CurrentDirectory;
            var dllPath = Path.Combine(baseDir, DllName);
            if (!File.Exists(dllPath))
            {
                // 不抛出异常，转换为可检查的初始化失败，方便 UI 诊断
                _lastError = $"Native library not found: {dllPath}";
                IsInitialized = false;
                return;
            }

            try
            {
                IsInitialized = device_open() == 1;
                if (!IsInitialized) _lastError = "device_open returned 0";
            }
            catch (DllNotFoundException ex)
            {
                IsInitialized = false;
                _lastError = "DllNotFoundException: " + ex.Message;
            }
            catch (Exception ex)
            {
                IsInitialized = false;
                _lastError = ex.GetType().Name + ": " + ex.Message;
            }
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "device_open")]
        private static extern int device_open();

        // button: 1=左键, 2=中键, 3=右键, 4=侧键后, 5=侧键前
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mouse_down")]
        private static extern void mouse_down(int button);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mouse_up")]
        private static extern void mouse_up(int button);

        // key: ANSI 键名字符串，如 "a", "lctrl", "space", "lshift" 等
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "key_down", CharSet = CharSet.Ansi)]
        private static extern void key_down([MarshalAs(UnmanagedType.LPStr)] string key);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "key_up", CharSet = CharSet.Ansi)]
        private static extern void key_up([MarshalAs(UnmanagedType.LPStr)] string key);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "moveR")]
        private static extern void moveR(int x, int y, int absMove);

        /// <summary>
        /// WPF Key 枚举到 ghub_device.dll 键名字符串的映射表。
        /// ghub 键名参考 GHUB 脚本文档。
        /// </summary>
        public static readonly IReadOnlyDictionary<Key, string> KeyMap =
            new Dictionary<Key, string>
            {
                // 字母键
                { Key.A, "a" }, { Key.B, "b" }, { Key.C, "c" }, { Key.D, "d" },
                { Key.E, "e" }, { Key.F, "f" }, { Key.G, "g" }, { Key.H, "h" },
                { Key.I, "i" }, { Key.J, "j" }, { Key.K, "k" }, { Key.L, "l" },
                { Key.M, "m" }, { Key.N, "n" }, { Key.O, "o" }, { Key.P, "p" },
                { Key.Q, "q" }, { Key.R, "r" }, { Key.S, "s" }, { Key.T, "t" },
                { Key.U, "u" }, { Key.V, "v" }, { Key.W, "w" }, { Key.X, "x" },
                { Key.Y, "y" }, { Key.Z, "z" },
                // 数字行
                { Key.D0, "0" }, { Key.D1, "1" }, { Key.D2, "2" }, { Key.D3, "3" },
                { Key.D4, "4" }, { Key.D5, "5" }, { Key.D6, "6" }, { Key.D7, "7" },
                { Key.D8, "8" }, { Key.D9, "9" },
                // 小键盘
                { Key.NumPad0, "numpad0" }, { Key.NumPad1, "numpad1" }, { Key.NumPad2, "numpad2" },
                { Key.NumPad3, "numpad3" }, { Key.NumPad4, "numpad4" }, { Key.NumPad5, "numpad5" },
                { Key.NumPad6, "numpad6" }, { Key.NumPad7, "numpad7" }, { Key.NumPad8, "numpad8" },
                { Key.NumPad9, "numpad9" },
                { Key.Multiply, "numpad*" }, { Key.Add, "numpad+" },
                { Key.Subtract, "numpad-" }, { Key.Decimal, "numpad." }, { Key.Divide, "numpad/" },
                // 功能键
                { Key.F1,  "f1"  }, { Key.F2,  "f2"  }, { Key.F3,  "f3"  }, { Key.F4,  "f4"  },
                { Key.F5,  "f5"  }, { Key.F6,  "f6"  }, { Key.F7,  "f7"  }, { Key.F8,  "f8"  },
                { Key.F9,  "f9"  }, { Key.F10, "f10" }, { Key.F11, "f11" }, { Key.F12, "f12" },
                // 修饰键
                { Key.LeftCtrl,   "lctrl"  }, { Key.RightCtrl,  "rctrl"  },
                { Key.LeftShift,  "lshift" }, { Key.RightShift, "rshift" },
                { Key.LeftAlt,    "lalt"   }, { Key.RightAlt,   "ralt"   },
                { Key.LWin,       "lwin"   }, { Key.RWin,       "rwin"   },
                // 控制键
                { Key.Escape,     "esc"    }, { Key.Tab,       "tab"        },
                { Key.CapsLock,   "capslock"  }, { Key.Back,      "backspace"  },
                { Key.Enter,      "enter"     }, { Key.Space,     "space"      },
                { Key.Insert,     "insert"    }, { Key.Delete,    "delete"     },
                { Key.Home,       "home"      }, { Key.End,       "end"        },
                { Key.PageUp,     "pageup"    }, { Key.PageDown,  "pagedown"   },
                { Key.PrintScreen,"printscreen"},{ Key.Scroll,    "scrolllock" },
                { Key.Pause,      "pause"     }, { Key.NumLock,   "numlock"    },
                // 方向键
                { Key.Up, "up" }, { Key.Down, "down" }, { Key.Left, "left" }, { Key.Right, "right" },
                // 标点符号
                { Key.OemMinus,     "-"  }, { Key.OemPlus,      "="  },
                { Key.OemOpenBrackets, "[" }, { Key.Oem6,       "]"  },
                { Key.Oem5,         "\\" }, { Key.OemSemicolon, ";"  },
                { Key.OemQuotes,    "'"  }, { Key.OemComma,     ","  },
                { Key.OemPeriod,    "."  }, { Key.OemQuestion,  "/"  },
                { Key.Oem3,         "`"  },
            };

        public static bool DeviceOpen()
        {
            try
            {
                if (IsInitialized)
                {
                    return IsInitialized;
                }
                IsInitialized = device_open() == 1;
            }
            catch
            {
                IsInitialized = false;
            }
            return IsInitialized;
        }

        /// <summary>
        /// 1=左键, 2=中键, 3=右键, 4=侧键后, 5=侧键前
        /// </summary>
        /// <param name="button"></param>
        public static void MouseDown(int button)
        {
            if (!IsInitialized) return;
            mouse_down(button);
        }

        /// <summary>
        /// 1=左键, 2=中键, 3=右键, 4=侧键后, 5=侧键前
        /// </summary>
        /// <param name="button"></param>
        public static void MouseUp(int button)
        {
            if (!IsInitialized) return;
            mouse_up(button);
        }

        /// <summary>
        /// 1=左键, 2=中键, 3=右键, 4=侧键后, 5=侧键前
        /// </summary>
        /// <param name="button"></param>
        /// <param name="holdMs"></param>
        public static void ClickMouse(int button, int holdMs = 100)
        {
            if (!IsInitialized) return;
            mouse_down(button);
            System.Threading.Thread.Sleep(GetRandomizedDelay(holdMs));
            mouse_up(button);
        }

        public static void KeyDown(string key)
        {
            if (!IsInitialized) return;
            if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            key_down(key);
        }

        public static void KeyUp(string key)
        {
            if (!IsInitialized) return;
            if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            key_up(key);
        }

        public static void ClickKey(string key, int holdMs = 50)
        {
            if (!IsInitialized) return;
            if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            key_down(key);
            System.Threading.Thread.Sleep(GetRandomizedDelay(holdMs));
            key_up(key);
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        /// <summary>
        /// 相对移动鼠标（absMove=false）或移动到屏幕绝对坐标（absMove=true）。
        /// ghub_device.dll 的 moveR 仅支持相对移动，绝对模式通过 GetCursorPos 计算差值实现。
        /// </summary>
        public static void Move(int x, int y, bool absMove = false)
        {
            if (!IsInitialized) return;
            if (absMove)
            {
                if (GetCursorPos(out POINT cur))
                {
                    moveR(x - cur.X, y - cur.Y, 0);
                }
                // GetCursorPos 失败时静默跳过，避免误操作
            }
            else
            {
                moveR(x, y, 0);
            }
        }

        /// <summary>将 WPF Key 枚举转换为 ghub 键名，找不到时返回 ""。</summary>
        public static string ToGhubKey(Key key) =>
            KeyMap.TryGetValue(key, out var name) ? name : "";

        public static void KeyDown(Key key)
        {
            if (!IsInitialized) return;
            var name = ToGhubKey(key) ?? throw new ArgumentOutOfRangeException(nameof(key), $"No ghub mapping for Key.{key}");
            key_down(name);
        }

        public static void KeyUp(Key key)
        {
            if (!IsInitialized) return;
            var name = ToGhubKey(key) ?? throw new ArgumentOutOfRangeException(nameof(key), $"No ghub mapping for Key.{key}");
            key_up(name);
        }

        public static void ClickKey(Key key, int holdMs = 50)
        {
            if (!IsInitialized) return;
            var name = ToGhubKey(key) ?? throw new ArgumentOutOfRangeException(nameof(key), $"No ghub mapping for Key.{key}");
            key_down(name);
            System.Threading.Thread.Sleep(GetRandomizedDelay(holdMs));
            key_up(name);
        }
    }
}
