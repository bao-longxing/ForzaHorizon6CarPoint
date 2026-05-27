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
            var baseDir = Path.GetDirectoryName(AppContext.BaseDirectory) ?? Environment.CurrentDirectory;
            
            var dllPath = Path.Combine(baseDir, DllName);
            if (!File.Exists(dllPath))
            {
                // 不抛出异常，转换为可检查的初始化失败，方便 UI 诊断s
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

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "device_close")]
        private static extern void device_close();

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
                { Key.NumPad0, "numpad_0" }, { Key.NumPad1, "numpad_1" }, { Key.NumPad2, "numpad_2" },
                { Key.NumPad3, "numpad_3" }, { Key.NumPad4, "numpad_4" }, { Key.NumPad5, "numpad_5" },
                { Key.NumPad6, "numpad_6" }, { Key.NumPad7, "numpad_7" }, { Key.NumPad8, "numpad_8" },
                { Key.NumPad9, "numpad_9" },
                { Key.Multiply, "numpad_mul" }, { Key.Add, "numpad_plus" },
                { Key.Subtract, "numpad_minus" }, { Key.Decimal, "numpad_dec" }, { Key.Divide, "numpad_div" },
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
                { Key.Escape,     "esc"      }, { Key.Tab,       "tab"         },
                { Key.CapsLock,   "cap"      }, { Key.Back,      "back_space"  },
                { Key.Enter,      "enter"    }, { Key.Space,     "space"       },
                { Key.Insert,     "insert"   }, { Key.Delete,    "del"         },
                { Key.Home,       "home"     }, { Key.End,       "end"         },
                { Key.PageUp,     "page_up"  }, { Key.PageDown,  "page_down"   },
                { Key.PrintScreen,"printscreen"},{ Key.Scroll,    "scroll_lock" },
                { Key.Pause,      "pause"    }, { Key.NumLock,   "numlock"     },
                // 方向键
                { Key.Up, "up" }, { Key.Down, "down" }, { Key.Left, "left" }, { Key.Right, "right" },
                // 标点符号
                { Key.OemMinus,     "minus"  }, { Key.OemPlus,      "equal"  },
                { Key.OemOpenBrackets, "square_bracket_left" }, { Key.Oem6,       "square_bracket_right"  },
                { Key.Oem5,         "back_slash" }, { Key.OemSemicolon, "column"  },
                { Key.OemQuotes,    "quote"  }, { Key.OemComma,     "comma"  },
                { Key.OemPeriod,    "period"  }, { Key.OemQuestion,  "slash"  },
                { Key.Oem3,         "back_tick"  }, { Key.Apps, "apps" },
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
        /// 关闭设备并清理状态。
        /// </summary>
        public static void DeviceClose()
        {
            if (!IsInitialized) return;
            try
            {
                device_close();
            }
            catch
            {
                // 忽略本地关闭时可能抛出的异常
            }
            IsInitialized = false;
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
            moveR(x, y, absMove ? 1 : 0);
        }

        /// <summary>
        /// 使用键盘输入文本字符串（用于输入蓝图代码等场景）。
        /// 通过 KeyMap 和 Shift 组合来实现各种字符的输入，每个字符之间间隔10ms。
        /// 支持字母、数字、常见符号以及小键盘。
        /// </summary>
        /// <param name="text">要输入的文本</param>
        public static void InputText(string text)
        {
            if (!IsInitialized || string.IsNullOrEmpty(text))
                return;

            // 创建反向 KeyMap（从 ghub key string 到 Key 枚举）
            var reverseKeyMap = new Dictionary<string, Key>();
            foreach (var kvp in KeyMap)
            {
                if (!reverseKeyMap.ContainsKey(kvp.Value))
                {
                    reverseKeyMap[kvp.Value] = kvp.Key;
                }
            }

            // 定义需要 Shift 的符号映射
            var shiftCharMap = new Dictionary<char, Key>
            {
                { '!', Key.D1 },   // Shift + 1
                { '@', Key.D2 },   // Shift + 2
                { '#', Key.D3 },   // Shift + 3
                { '$', Key.D4 },   // Shift + 4
                { '%', Key.D5 },   // Shift + 5
                { '^', Key.D6 },   // Shift + 6
                { '&', Key.D7 },   // Shift + 7
                { '*', Key.D8 },   // Shift + 8
                { '(', Key.D9 },   // Shift + 9
                { ')', Key.D0 },   // Shift + 0
                { '_', Key.OemMinus },     // Shift + -
                { '+', Key.OemPlus },      // Shift + =
                { '{', Key.Oem6 },         // Shift + [（根据键盘布局调整）
                { '}', Key.Oem5 },         // Shift + ]
                { '|', Key.Oem5 },         // Shift + \
                { ':', Key.OemSemicolon }, // Shift + ;
                { '"', Key.OemQuotes },    // Shift + '
                { '<', Key.OemComma },     // Shift + ,
                { '>', Key.OemPeriod },    // Shift + .
                { '?', Key.OemQuestion },  // Shift + /
                { '~', Key.Oem3 },         // Shift + `
            };

            foreach (var ch in text)
            {
                try
                {
                    // 如果是大写字母，需要按 Shift
                    if (char.IsUpper(ch))
                    {
                        Key key = (Key)Enum.Parse(typeof(Key), ch.ToString(), true);
                        if (KeyMap.ContainsKey(key))
                        {
                            KeyDown(Key.LeftShift);
                            Thread.Sleep(10);
                            ClickKey(key, 50);
                            KeyUp(Key.LeftShift);
                        }
                    }
                    // 小写字母
                    else if (char.IsLower(ch))
                    {
                        Key key = (Key)Enum.Parse(typeof(Key), ch.ToString(), true);
                        if (KeyMap.ContainsKey(key))
                        {
                            ClickKey(key, 50);
                        }
                    }
                    // 数字
                    else if (char.IsDigit(ch))
                    {
                        Key key = (Key)Enum.Parse(typeof(Key), "D" + ch, true);
                        if (KeyMap.ContainsKey(key))
                        {
                            ClickKey(key, 50);
                        }
                    }
                    // 特殊符号（需要 Shift）
                    else if (shiftCharMap.TryGetValue(ch, out Key shiftKey))
                    {
                        KeyDown(Key.LeftShift);
                        Thread.Sleep(10);
                        ClickKey(shiftKey, 50);
                        KeyUp(Key.LeftShift);
                    }
                    // 直接映射的符号
                    else if (reverseKeyMap.TryGetValue(ch.ToString(), out Key directKey))
                    {
                        ClickKey(directKey, 50);
                    }
                    // 空格
                    else if (ch == ' ')
                    {
                        ClickKey(Key.Space, 50);
                    }

                    Thread.Sleep(10);
                }
                catch
                {
                    // 如果某个字符失败，继续处理下一个字符
                    continue;
                }
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
