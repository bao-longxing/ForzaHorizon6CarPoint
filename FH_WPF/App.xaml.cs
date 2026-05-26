using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace FH_WPF
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // 检查是否以管理员身份运行
            if (!IsRunAsAdministrator())
            {
                var res = MessageBox.Show("需要以管理员权限运行。现在以管理员身份重新启动？", "权限不足", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res == MessageBoxResult.Yes)
                {
                    try
                    {
                        var exe = Process.GetCurrentProcess().MainModule?.FileName;
                        if (!string.IsNullOrEmpty(exe))
                        {
                            var psi = new ProcessStartInfo(exe)
                            {
                                UseShellExecute = true,
                                Verb = "runas"
                            };
                            Process.Start(psi);
                        }
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // 用户取消或提升失败，继续退出当前应用
                    }
                }

                Shutdown();
                return;
            }

            base.OnStartup(e);
        }

        private bool IsRunAsAdministrator()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 1. 先触发你自己的静态资源清理
            ClsLogicContorl_Ghub.DeviceClose();

            // 2. 不要忘了调用基类方法
            base.OnExit(e);
        }

    }

}
