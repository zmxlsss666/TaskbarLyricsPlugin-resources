using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TaskbarLyrics
{
    /// <summary>
    /// 任务栏监控器类
    /// 负责：
    /// 1. 获取Windows任务栏的位置和大小
    /// 2. 设置窗口的特殊样式（透明、穿透、置顶等）
    /// 3. 管理窗口在任务栏上的定位
    /// 4. 处理鼠标事件的穿透控制
    /// 使用Win32 API实现底层窗口管理功能
    /// </summary>
    public class TaskbarMonitor
    {
        #region Win32 API 导入

        /// <summary>
        /// 查找窗口句柄
        /// </summary>
        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        /// <summary>
        /// 获取窗口矩形区域
        /// </summary>
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rectangle);

        /// <summary>
        /// 设置窗口位置和样式
        /// </summary>
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        /// <summary>
        /// 获取窗口扩展样式
        /// </summary>
        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        /// <summary>
        /// 设置窗口扩展样式
        /// </summary>
        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        /// <summary>
        /// 显示或隐藏窗口
        /// </summary>
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        /// <summary>
        /// 检查窗口是否可见
        /// </summary>
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        #endregion

        #region 结构体和常量

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

        // 窗口扩展样式常量
        public const int GWL_EXSTYLE = -20;              // 获取扩展样式索引
        public const int WS_EX_LAYERED = 0x80000;       // 分层窗口
        public const int WS_EX_TRANSPARENT = 0x20;      // 鼠标穿透
        public const int WS_EX_NOACTIVATE = 0x08000000; // 不激活窗口
        public const int WS_EX_TOOLWINDOW = 0x00000080; // 不在任务栏显示
        public const int WS_EX_TOPMOST = 0x00000008;    // 置顶窗口

        // SetWindowPos 参数常量
        private const int HWND_TOPMOST = -1;             // 置顶句柄
        private const uint SWP_NOSIZE = 0x0001;         // 不改变大小
        private const uint SWP_NOMOVE = 0x0002;         // 不改变位置
        private const uint SWP_NOACTIVATE = 0x0010;      // 不激活窗口
        private const uint SWP_SHOWWINDOW = 0x0040;      // 显示窗口

        // ShowWindow 参数常量
        private const int SW_SHOW = 5;                   // 正常显示
        private const int SW_SHOWNA = 8;                 // 不激活显示

        #endregion

        #region 任务栏信息获取

        /// <summary>
        /// 获取任务栏的矩形区域
        /// 自动处理DPI缩放
        /// </summary>
        /// <returns>任务栏的Rect对象</returns>
        public static Rect GetTaskbarRect()
        {
            // 查找任务栏窗口（类名为 "Shell_TrayWnd"）
            IntPtr taskbarHandle = FindWindow("Shell_TrayWnd", null);

            if (taskbarHandle != IntPtr.Zero && GetWindowRect(taskbarHandle, out RECT rect))
            {
                // 获取DPI缩放比例
                double dpiScale = GetDpiScale();

                // 转换为WPF单位（考虑DPI）
                return new Rect(
                    rect.Left / dpiScale,
                    rect.Top / dpiScale,
                    (rect.Right - rect.Left) / dpiScale,
                    (rect.Bottom - rect.Top) / dpiScale);
            }

            // 如果获取任务栏失败，使用默认值（底部40像素的任务栏）
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            double taskbarHeight = 40;

            return new Rect(0, screenHeight - taskbarHeight, screenWidth, taskbarHeight);
        }

        /// <summary>
        /// 获取DPI缩放比例
        /// 确保在不同DPI设置下窗口大小正确
        /// </summary>
        /// <returns>DPI缩放比例</returns>
        private static double GetDpiScale()
        {
            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow != null)
            {
                var source = PresentationSource.FromVisual(mainWindow);
                if (source?.CompositionTarget != null)
                {
                    // TransformToDevice.M11 是X轴的缩放比例
                    return source.CompositionTarget.TransformToDevice.M11;
                }
            }

            // 默认不缩放
            return 1.0;
        }

        #endregion

        #region 窗口定位

        /// <summary>
        /// 将窗口定位到任务栏（无偏移）
        /// </summary>
        /// <param name="window">要定位的窗口</param>
        public static void PositionWindowOnTaskbar(Window window)
        {
            PositionWindowOnTaskbar(window, 0, 0);
        }

        /// <summary>
        /// 将窗口定位到任务栏（带偏移）
        /// </summary>
        /// <param name="window">要定位的窗口</param>
        /// <param name="offsetX">X轴偏移量</param>
        /// <param name="offsetY">Y轴偏移量</param>
        public static void PositionWindowOnTaskbar(Window window, int offsetX, int offsetY)
        {
            var taskbarRect = GetTaskbarRect();

            // 设置窗口位置和大小与任务栏一致
            window.Left = taskbarRect.Left + offsetX;
            window.Top = taskbarRect.Top + offsetY;
            window.Width = taskbarRect.Width;
            window.Height = taskbarRect.Height;
        }

        #endregion

        #region 窗口样式设置

        /// <summary>
        /// 启用窗口透明效果
        /// 设置窗口为分层窗口，不激活，不在任务栏显示，置顶
        /// </summary>
        /// <param name="window">要设置样式的窗口</param>
        public static void EnableWindowTransparency(Window window)
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();

            // 获取当前扩展样式
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

            // 添加特殊样式标志
            SetWindowLong(hwnd, GWL_EXSTYLE,
                extendedStyle |
                WS_EX_LAYERED |        // 分层窗口（支持透明）
                WS_EX_NOACTIVATE |     // 不获取焦点
                WS_EX_TOOLWINDOW |     // 不在任务栏显示
                WS_EX_TOPMOST);        // 置顶显示
        }

        /// <summary>
        /// 将窗口设置到任务栏级别（置顶）
        /// </summary>
        /// <param name="window">要设置的窗口</param>
        public static void SetWindowToTaskbarLevel(Window window)
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();

            // 设置窗口位置（仅改变Z顺序，不改变实际位置）
            SetWindowPos(hwnd, (IntPtr)HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        /// <summary>
        /// 强制显示窗口
        /// 用于窗口被意外隐藏时的恢复
        /// </summary>
        /// <param name="window">要显示的窗口</param>
        public static void ForceShowWindow(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).EnsureHandle();

                // 以不激活的方式显示窗口
                ShowWindow(hwnd, SW_SHOWNA);

                // 重新应用透明样式
                EnableWindowTransparency(window);

                // 确保窗口置顶
                SetWindowPos(hwnd, (IntPtr)HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);

                Logger.Info("窗口已强制显示");
            }
            catch (Exception ex)
            {
                Logger.Error($"强制显示窗口时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查窗口是否可见
        /// </summary>
        /// <param name="window">要检查的窗口</param>
        /// <returns>窗口是否可见</returns>
        public static bool IsWindowVisible(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).EnsureHandle();
                return IsWindowVisible(hwnd);
            }
            catch
            {
                // 发生异常时认为窗口不可见
                return false;
            }
        }

        #endregion

        #region 鼠标事件控制

        /// <summary>
        /// 启用鼠标事件
        /// 移除鼠标穿透效果，使窗口可以接收鼠标交互
        /// </summary>
        /// <param name="window">要设置的窗口</param>
        public static void EnableMouseEvents(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).EnsureHandle();

                // 获取当前扩展样式
                int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

                // 移除透明标志，保留其他样式
                extendedStyle &= ~WS_EX_TRANSPARENT;

                // 重新应用样式（不含透明）
                SetWindowLong(hwnd, GWL_EXSTYLE,
                    extendedStyle |
                    WS_EX_LAYERED |
                    WS_EX_NOACTIVATE |
                    WS_EX_TOOLWINDOW |
                    WS_EX_TOPMOST);
            }
            catch (Exception ex)
            {
                Logger.Error($"启用鼠标事件时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 禁用鼠标事件
        /// 添加鼠标穿透效果，使窗口不响应鼠标交互
        /// </summary>
        /// <param name="window">要设置的窗口</param>
        public static void DisableMouseEvents(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).EnsureHandle();

                // 获取当前扩展样式
                int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

                // 添加透明标志（鼠标穿透）
                SetWindowLong(hwnd, GWL_EXSTYLE,
                    extendedStyle |
                    WS_EX_LAYERED |
                    WS_EX_TRANSPARENT |
                    WS_EX_NOACTIVATE |
                    WS_EX_TOOLWINDOW |
                    WS_EX_TOPMOST);
            }
            catch (Exception ex)
            {
                Logger.Error($"禁用鼠标事件时出错: {ex.Message}");
            }
        }

        #endregion
    }
}