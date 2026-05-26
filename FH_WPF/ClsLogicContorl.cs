//using System;
//using System.IO;
//using System.Reflection;
//using System.Runtime.InteropServices;

//namespace FH_WPF
//{
//    internal static class ClsLogicContorl 
//    {
//        private const string DllName = "MouseControl.dll";

//        static ClsLogicContorl()
//        {
//            // 确保本地 DLL 与可执行程序集位于同一目录，以便在缺失时提供更明确的错误信息
//            var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Environment.CurrentDirectory;
//            var dllPath = Path.Combine(baseDir, DllName);
//            if (!File.Exists(dllPath))
//            {
//                throw new FileNotFoundException($"Native library not found: {dllPath}", dllPath);
//            }
//        }

//        // P/Invoke 声明，使用 Cdecl 调用约定（本地 C 导出常用）
//        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "move_R")]
//        private static extern void move_R(int x, int y);

//        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "move_Abs")]
//        private static extern void move_Abs(int x, int y);

//        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "click_Left_down")]
//        private static extern void click_Left_down();

//        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "click_Left_up")]
//        private static extern void click_Left_up();

//        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "click_Right_down")]
//        private static extern void click_Right_down();

//        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "click_Right_up")]
//        private static extern void click_Right_up();

//        // 公共安全包装器
//        public static void MoveRelative(int dx, int dy) => move_R(dx, dy);

//        public static void MoveAbsolute(int x, int y) => move_Abs(x, y);

//        public static void LeftDown() => click_Left_down();

//        public static void LeftUp() => click_Left_up();

//        public static void RightDown() => click_Right_down();

//        public static void RightUp() => click_Right_up();

//        // 便捷助手：先移动然后点击
//        public static void ClickLeft(int x, int y, int holdMs = 50)
//        {
//            move_Abs(x, y);
//            click_Left_down();
//            System.Threading.Thread.Sleep(holdMs);
//            click_Left_up();
//        }

//        public static void ClickRight(int x, int y, int holdMs = 50)
//        {
//            move_Abs(x, y);
//            click_Right_down();
//            System.Threading.Thread.Sleep(holdMs);
//            click_Right_up();
//        }
//    }
//}
