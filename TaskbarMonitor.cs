using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TaskbarLyrics
{
    public class TaskbarMonitor
    {
        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rectangle);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int HWND_TOPMOST = -1;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int SW_SHOW = 5;
        private const int SW_SHOWNA = 8;

        public static Rect GetTaskbarRect()
        {
            IntPtr taskbarHandle = FindWindow("Shell_TrayWnd", null);
            
            if (taskbarHandle != IntPtr.Zero && GetWindowRect(taskbarHandle, out RECT rect))
            {

                double dpiScale = GetDpiScale();
                return new Rect(
                    rect.Left / dpiScale,
                    rect.Top / dpiScale,
                    (rect.Right - rect.Left) / dpiScale,
                    (rect.Bottom - rect.Top) / dpiScale);
            }

            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            double taskbarHeight = 40; 
            
            return new Rect(0, screenHeight - taskbarHeight, screenWidth, taskbarHeight);
        }

        private static double GetDpiScale()
        {

            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow != null)
            {
                var source = PresentationSource.FromVisual(mainWindow);
                if (source?.CompositionTarget != null)
                {
                    return source.CompositionTarget.TransformToDevice.M11;
                }
            }
            
            return 1.0;
        }

        public static void PositionWindowOnTaskbar(Window window)
        {
            var taskbarRect = GetTaskbarRect();
            
            window.Left = taskbarRect.Left;
            window.Top = taskbarRect.Top;
            window.Width = taskbarRect.Width;
            window.Height = taskbarRect.Height;
        }

        public static void EnableWindowTransparency(Window window)
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            
            SetWindowLong(hwnd, GWL_EXSTYLE, 
                extendedStyle | 
                WS_EX_LAYERED | 
                WS_EX_TRANSPARENT | 
                WS_EX_NOACTIVATE |
                WS_EX_TOOLWINDOW |
                WS_EX_TOPMOST);
        }

        public static void SetWindowToTaskbarLevel(Window window)
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            
            SetWindowPos(hwnd, (IntPtr)HWND_TOPMOST, 0, 0, 0, 0, 
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        public static void ForceShowWindow(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).EnsureHandle();
                
                ShowWindow(hwnd, SW_SHOWNA);
                
                EnableWindowTransparency(window);
	
                SetWindowPos(hwnd, (IntPtr)HWND_TOPMOST, 0, 0, 0, 0, 
                    SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                
                Debug.WriteLine("On");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error: {ex.Message}");
            }
        }

        public static bool IsWindowVisible(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).EnsureHandle();
                return IsWindowVisible(hwnd);
            }
            catch
            {
                return false;
            }
        }
    }
}