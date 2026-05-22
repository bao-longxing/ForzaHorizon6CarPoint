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
//            // Ensure the native DLL exists next to the executing assembly to provide a clearer error
//            var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Environment.CurrentDirectory;
//            var dllPath = Path.Combine(baseDir, DllName);
//            if (!File.Exists(dllPath))
//            {
//                throw new FileNotFoundException($"Native library not found: {dllPath}", dllPath);
//            }
//        }

//        // P/Invoke declarations. Using Cdecl which is the common default for native C exports.
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

//        // Public safe wrappers
//        public static void MoveRelative(int dx, int dy) => move_R(dx, dy);

//        public static void MoveAbsolute(int x, int y) => move_Abs(x, y);

//        public static void LeftDown() => click_Left_down();

//        public static void LeftUp() => click_Left_up();

//        public static void RightDown() => click_Right_down();

//        public static void RightUp() => click_Right_up();

//        // Convenience helpers: move then click
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
