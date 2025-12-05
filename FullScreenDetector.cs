using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Forms;

namespace TaskbarLyrics
{
    /// <summary>
    /// 全屏检测器类
    /// 负责：
    /// 1. 检测是否有应用程序处于全屏模式
    /// 2. 在全屏时自动隐藏歌词窗口
    /// 3. 退出全屏时恢复显示
    /// 使用定时器定期检查前台窗口状态
    /// </summary>
    public static class FullScreenDetector
    {
        #region 私有字段

        /// <summary>
        /// 定时器，用于定期检查全屏状态
        /// 使用Windows Forms Timer以确保在UI线程上执行
        /// </summary>
        private static System.Windows.Forms.Timer _checkTimer;

        /// <summary>
        /// 当前是否处于全屏模式
        /// </summary>
        private static bool _isFullScreenActive = false;

        #endregion

        #region 公共事件和属性

        /// <summary>
        /// 全屏状态变化事件
        /// 参数：sender, isFullScreenActive
        /// </summary>
        public static event EventHandler<bool> FullScreenStatusChanged;

        /// <summary>
        /// 获取当前是否处于全屏模式
        /// </summary>
        public static bool IsFullScreenActive => _isFullScreenActive;

        #endregion

        #region 静态构造函数

        /// <summary>
        /// 静态构造函数
        /// 初始化定时器
        /// </summary>
        static FullScreenDetector()
        {
            _checkTimer = new System.Windows.Forms.Timer
            {
                Interval = 1000 // 每1秒检查一次，避免过于频繁的检查
            };
            _checkTimer.Tick += CheckFullScreenStatus;
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 启动全屏检测
        /// 如果已经启动则忽略
        /// </summary>
        public static void Start()
        {
            if (!_checkTimer.Enabled)
            {
                _checkTimer.Start();
                // 立即检查一次，确保初始状态正确
                CheckFullScreenStatus(null, null);
            }
        }

        /// <summary>
        /// 停止全屏检测功能
        /// </summary>
        public static void Stop()
        {
            _checkTimer.Stop(); // 停止计时器
            _isFullScreenActive = false; // 重置全屏状态标志
            Logger.Info("全屏检测已停止"); // 记录停止信息到日志
        }

        /// <summary>
        /// 获取当前活动窗口的标题
        /// 用于调试和日志记录
        /// </summary>
        /// <returns>窗口标题字符串</returns>
        public static string GetActiveWindowTitle()
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero)
                return "未知窗口";

            var title = new StringBuilder(256);
            GetWindowText(hWnd, title, title.Capacity);
            return title.ToString();
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 定时器回调方法，检查全屏状态
        /// </summary>
        private static void CheckFullScreenStatus(object sender, EventArgs e)
        {
            try
            {
                // 检查当前前台窗口是否为全屏
                bool currentFullScreenStatus = IsFullScreenWindow();

                // 如果状态发生变化
                if (currentFullScreenStatus != _isFullScreenActive)
                {
                    _isFullScreenActive = currentFullScreenStatus;

                    // 记录状态变化
                    if (currentFullScreenStatus)
                    {
                        Logger.Info($"检测到全屏应用: {GetActiveWindowTitle()}");
                    }
                    else
                    {
                        Logger.Info("退出全屏模式");
                    }

                    // 触发状态变化事件
                    FullScreenStatusChanged?.Invoke(null, _isFullScreenActive);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"全屏检测错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 判断指定窗口是否为全屏窗口
        /// 检查窗口大小、样式等特征
        /// </summary>
        /// <returns>是否为全屏窗口</returns>
        private static bool IsFullScreenWindow()
        {
            IntPtr hWnd = GetForegroundWindow();

            if (hWnd == IntPtr.Zero)
            {
                return false;
            }

            // 获取窗口标题用于调试
            var title = new StringBuilder(256);
            GetWindowText(hWnd, title, title.Capacity);
            string windowTitle = title.ToString();

            // 检查窗口是否可见
            if (!IsWindowVisible(hWnd))
            {
                return false;
            }

            // 获取窗口信息
            if (!GetWindowRect(hWnd, out RECT windowRect))
            {
                return false;
            }

            int windowWidth = windowRect.Right - windowRect.Left;
            int windowHeight = windowRect.Bottom - windowRect.Top;

            // 获取屏幕信息
            RECT screenRect = GetScreenRect();
            int screenWidth = screenRect.Right - screenRect.Left;
            int screenHeight = screenRect.Bottom - screenRect.Top;

            // 检查窗口是否覆盖整个屏幕（允许小的误差）
            const int tolerance = 10; // 允许10像素的误差
            bool isFullScreenSize = (windowWidth >= screenWidth - tolerance &&
                                    windowHeight >= screenHeight - tolerance);

            if (!isFullScreenSize)
            {
                return false;
            }

            // 检查窗口样式，确保不是系统窗口
            if (!ShouldConsiderAsFullScreen(hWnd))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 判断窗口是否应该被视为全屏窗口
        /// 排除系统窗口、桌面等特殊情况
        /// </summary>
        /// <param name="hWnd">窗口句柄</param>
        /// <returns>是否应该视为全屏窗口</returns>
        private static bool ShouldConsiderAsFullScreen(IntPtr hWnd)
        {
            // 获取窗口类名
            var className = new StringBuilder(256);
            GetClassName(hWnd, className, className.Capacity);
            string windowClassName = className.ToString();

            // 排除系统窗口
            if (windowClassName.Equals("Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||  // 任务栏
                windowClassName.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||        // 桌面
                windowClassName.Equals("WorkerW", StringComparison.OrdinalIgnoreCase) ||         // 桌面
                windowClassName.Equals("DV2ControlHost", StringComparison.OrdinalIgnoreCase) ||  // 任务视图
                windowClassName.Contains("Windows.UI.Core.CoreWindow"))                         // UWP 系统窗口
            {
                return false;
            }

            // 获取窗口标题
            var title = new StringBuilder(256);
            GetWindowText(hWnd, title, title.Capacity);
            string windowTitle = title.ToString();

            // 排除空标题的窗口
            if (string.IsNullOrWhiteSpace(windowTitle))
                return false;

            // 检查窗口是否具有典型应用程序特征
            long exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);

            // 排除工具窗口和纯顶层窗口
            if ((exStyle & WS_EX_TOOLWINDOW) != 0)
                return false;

            return true;
        }

        /// <summary>
        /// 获取窗口所在屏幕的矩形区域
        /// 支持多显示器环境
        /// </summary>
        /// <returns>屏幕矩形</returns>
        private static RECT GetScreenRect()
        {
            // 获取窗口所在屏幕的信息
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd != IntPtr.Zero)
            {
                HMONITOR hMonitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);

                if (hMonitor != IntPtr.Zero)
                {
                    MONITORINFO monitorInfo = new MONITORINFO();
                    monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));

                    if (GetMonitorInfo(hMonitor, ref monitorInfo))
                    {
                        return monitorInfo.rcMonitor;
                    }
                }
            }

            // 如果无法获取特定屏幕，返回主屏幕
            return new RECT
            {
                Left = 0,
                Top = 0,
                Right = Screen.PrimaryScreen.Bounds.Width,
                Bottom = Screen.PrimaryScreen.Bounds.Height
            };
        }

        #endregion

        #region Win32 API 声明

        /// <summary>
        /// 获取前台窗口句柄
        /// </summary>
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetForegroundWindow();

        /// <summary>
        /// 获取窗口标题
        /// </summary>
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        /// <summary>
        /// 获取窗口类名
        /// </summary>
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        /// <summary>
        /// 检查窗口是否可见
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        /// <summary>
        /// 获取窗口矩形区域
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        /// <summary>
        /// 获取窗口扩展样式
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        private static extern long GetWindowLong(IntPtr hWnd, int nIndex);

        /// <summary>
        /// 获取窗口所在的显示器句柄
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        private static extern HMONITOR MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        /// <summary>
        /// 获取显示器信息
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetMonitorInfo(HMONITOR hMonitor, ref MONITORINFO lpmi);

        #endregion

        #region 结构体

        /// <summary>
        /// 矩形结构体
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>
        /// 显示器信息结构体
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;  // 显示器矩形
            public RECT rcWork;     // 工作区矩形
            public uint dwFlags;    // 标志位
        }

        /// <summary>
        /// 显示器句柄包装
        /// </summary>
        private readonly struct HMONITOR
        {
            private readonly IntPtr handle;
            private HMONITOR(IntPtr handle) => this.handle = handle;
            public static implicit operator HMONITOR(IntPtr handle) => new HMONITOR(handle);
            public static implicit operator IntPtr(HMONITOR hMonitor) => hMonitor.handle;
        }

        #endregion

        #region 常量

        /// <summary>
        /// 获取窗口扩展样式的索引
        /// </summary>
        private const int GWL_EXSTYLE = -20;

        /// <summary>
        /// 工具窗口样式
        /// </summary>
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        /// <summary>
        /// 获取最近的显示器
        /// </summary>
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        #endregion
    }
}