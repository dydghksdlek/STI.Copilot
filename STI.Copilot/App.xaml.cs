using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace STI.Copilot
{
    /// <summary>
    /// App.xaml에 대한 상호 작용 논리.
    /// 이름 있는 Mutex로 중복 실행을 막는다. 이미 실행 중이면 기존 창을 앞으로 가져오고 종료.
    /// </summary>
    public partial class App : Application
    {
        // 사용자 세션 단위로 유일한 이름(고정 GUID). 프로세스 수명 동안 보유해 중복 실행을 차단.
        private const string MutexName = "STI.Copilot.SingleInstance.{9F3B2C14-7A6E-4E2D-9B1A-STICOPILOT}";
        private static Mutex _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                // 이미 실행 중 → 기존 창을 앞으로 가져오고 이 인스턴스는 조용히 종료.
                ActivateExistingInstance();
                Shutdown();
                return;
            }

            base.OnStartup(e);
        }

        // 같은 이름의 다른 프로세스를 찾아 창을 복원·전면화한다.
        private static void ActivateExistingInstance()
        {
            try
            {
                var me = Process.GetCurrentProcess();
                var other = Process.GetProcessesByName(me.ProcessName)
                    .FirstOrDefault(p => p.Id != me.Id && p.MainWindowHandle != IntPtr.Zero);
                if (other != null)
                {
                    var h = other.MainWindowHandle;
                    if (IsIconic(h)) ShowWindow(h, SW_RESTORE); // 최소화돼 있으면 복원
                    SetForegroundWindow(h);
                }
            }
            catch { /* 전면화 실패는 무시 — 중복 실행 차단이 핵심 */ }
        }

        private const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
    }
}
