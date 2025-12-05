using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TaskbarLyrics.Models;

namespace TaskbarLyrics
{
    /// <summary>
    /// 主窗口类 - 负责在Windows任务栏上显示歌词
    /// 主要功能：
    /// 1. 从本地API获取歌词数据并显示
    /// 2. 实现逐字同步动画效果
    /// 3. 提供播放控制功能
    /// 4. 支持自定义字体、颜色和对齐方式
    /// 5. 全屏时自动隐藏
    /// </summary>
    public partial class MainWindow : Window
    {
        #region 私有字段

        // API服务 - 用于与本地歌词API服务器通信
        private LyricsApiService _apiService;

        // 定时器管理 - 用于不同功能的时间控制
        private DispatcherTimer _restoreTimer;     // 窗口状态恢复定时器（100ms间隔）
        private DispatcherTimer _nowPlayingTimer;  // 播放状态更新定时器（50ms间隔）
        private DispatcherTimer _mouseLeaveTimer;  // 鼠标离开延迟处理定时器（300ms间隔）
        private DispatcherTimer _smoothUpdateTimer; // 平滑更新定时器（32ms间隔，约30FPS）

        // 歌词数据管理
        private List<LyricsLine> _lyricsLines = new List<LyricsLine>();  // 解析后的歌词行列表
        private string _lastLyricsText = "";      // 上次获取的歌词文本，用于避免重复解析
        private bool _forceRefresh = false;       // 强制刷新标志，用于需要立即更新歌词的场景
        private int _currentPosition = 0;         // 当前播放位置（毫秒）
        private int _lastLyricsLineCount = 0;     // 上次歌词行数，用于避免重复的日志输出
        private bool _isPlaying = false;          // 当前播放状态

        // 窗口和鼠标状态
        private bool _isClosing = false;          // 窗口是否正在关闭
        private bool _isMouseOver = false;        // 鼠标是否悬停在窗口上

        // 歌词变化检测
        private string _lastSongTitle = "";       // 上次的歌曲标题，用于检测歌曲变化
        private LyricsLine _lastLyricsLine = null; // 上次的歌词行，用于检测歌词行变化

        // 渲染缓存 - 优化性能，避免重复创建相同的视觉元素
        private string _lastCachedLyricsKey = "";     // 缓存键（歌词内容+位置）
        private FrameworkElement _lastCachedVisual = null; // 缓存的视觉元素

        #endregion

        #region 构造函数与窗口事件

        /// <summary>
        /// 主窗口构造函数
        /// 初始化所有必要的组件和服务
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            // 初始化API服务，用于与本地歌词API服务器通信
            _apiService = new LyricsApiService();

            // 订阅窗口事件
            this.IsVisibleChanged += MainWindow_IsVisibleChanged;  // 窗口可见性变化事件
            this.Closing += MainWindow_Closing;                    // 窗口关闭事件

            // 初始化核心功能
            InitializeWindow();          // 初始化窗口属性和样式
            SetupTimers();              // 设置所有定时器
            SetupFullScreenDetection(); // 设置全屏检测

            // 设置窗口交互属性
            this.Focusable = true;        // 允许窗口获得焦点
            this.IsHitTestVisible = true; // 允许鼠标交互
        }

        /// <summary>
        /// 窗口关闭事件处理程序
        /// 清理所有资源，停止定时器和服务
        /// </summary>
        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 设置关闭标志，通知其他方法停止处理
            _isClosing = true;

            // 停止所有定时器，防止内存泄漏
            _restoreTimer?.Stop();
            _nowPlayingTimer?.Stop();
            _smoothUpdateTimer?.Stop();
            _mouseLeaveTimer?.Stop();

            // 停止全屏检测服务
            FullScreenDetector.Stop();
        }

        #endregion

        /// <summary>
        /// 窗口可见性变化事件处理程序
        /// 确保窗口在全屏模式下不会自动恢复显示
        /// </summary>
        private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // 如果窗口正在关闭，不做任何处理
            if (_isClosing)
                return;

            // 如果全屏检测正在运行且已检测到全屏，不要自动恢复可见性
            // 这样可以避免在全屏应用（如游戏、视频播放器）运行时弹出歌词
            if (FullScreenDetector.IsFullScreenActive && ConfigManager.CurrentConfig.HideOnFullscreen)
            {
                Logger.Info("全屏模式激活，跳过自动可见性恢复");
                return;
            }

            // 如果窗口不可见，尝试自动恢复可见性
            // 使用异步调用避免与UI线程冲突
            if (!this.IsVisible)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // 双重检查确保窗口确实需要恢复且未关闭
                    if (!_isClosing && !this.IsVisible)
                    {
                        this.Visibility = Visibility.Visible;
                        TaskbarMonitor.ForceShowWindow(this);
                        Logger.Info("自动恢复窗口可见性");
                    }
                }), DispatcherPriority.Background);
            }
        }

        #region 定时器设置

        /// <summary>
        /// 设置所有定时器
        /// 每个定时器负责不同的功能，确保程序的各种操作按时执行
        /// </summary>
        private void SetupTimers()
        {
            // 窗口状态恢复定时器 - 每100ms检查并恢复窗口状态
            // 确保窗口始终保持置顶和可见状态
            _restoreTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _restoreTimer.Tick += (s, e) => EnsureWindowOnTop();
            _restoreTimer.Start();

            // 播放状态更新定时器 - 每50ms更新一次播放状态
            // 使用高频更新以确保歌词同步的精确性

            // 这是真tm的屎山，高频的http请求浪费大量资源
            // 已经优化掉两个高频的http请求了，剩下这个不好改
            // 不知道原作者为啥要这样实现，最好能改成sse或者websocket
            // 但是需要配合插件端更改，先不动这个了，目前没有精力维护
            _nowPlayingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _nowPlayingTimer.Tick += async (s, e) => await UpdateNowPlaying();
            _nowPlayingTimer.Start();

            // 平滑更新定时器 - 每32ms更新一次（约30FPS）
            // 负责歌词的平滑显示和动画效果
            _smoothUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(32) };
            _smoothUpdateTimer.Tick += (s, e) => SmoothUpdateLyrics();
            _smoothUpdateTimer.Start();

            // 鼠标离开延迟定时器 - 300ms延迟
            // 用于检测鼠标是否真正离开窗口，避免误触发
            _mouseLeaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _mouseLeaveTimer.Tick += (s, e) =>
            {
                _mouseLeaveTimer.Stop();
                // 确保鼠标确实不在窗口上才隐藏控制面板
                if (!_isMouseOver)
                {
                    HideControlPanel();
                }
            };
        }

        #endregion

        /// <summary>
        /// 更新当前播放状态
        /// 高频执行（每50ms），负责：
        /// 1. 获取当前播放位置
        /// 2. 检测歌曲变化，并在变化时触发歌词更新
        /// 3. 更新播放按钮状态
        /// </summary>
        private async Task UpdateNowPlaying()
        {
            try
            {
                // 从API获取当前播放信息
                var nowPlaying = await _apiService.GetNowPlayingAsync();
                if (nowPlaying?.Status == "success")
                {
                    // 更新播放位置和状态
                    _currentPosition = nowPlaying.Position;
                    _isPlaying = nowPlaying.IsPlaying;

                    // 检测歌曲变化 - 通过组合艺术家和标题来判断
                    string currentSongTitle = $"{nowPlaying.Artist} - {nowPlaying.Title}";
                    if (!string.IsNullOrEmpty(currentSongTitle) && currentSongTitle != _lastSongTitle)
                    {
                        Logger.Info($"检测到歌曲变化: {_lastSongTitle} -> {currentSongTitle}");
                        _lastSongTitle = currentSongTitle;

                        // 歌曲变化时的清理工作
                        ClearLyrics();           // 清空显示的歌词
                        _lastLyricsText = "";   // 重置歌词文本缓存
                        _lyricsLines.Clear();   // 清空解析的歌词列表
                        _lastLyricsLine = null; // 重置歌词行跟踪
                        _forceRefresh = true;   // 设置强制刷新标志

                        // 歌曲变化时触发歌词更新（不再使用定时器）
                        await UpdateLyrics();
                    }

                    // 更新播放/暂停按钮的图标
                    PlayPauseButton.Content = _isPlaying ? "⏸" : "▶";
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"获取当前播放信息时出错: {ex.Message}");
            }
        }

        private void SmoothUpdateLyrics()
        {
            if (_lyricsLines.Count > 0 && !_isClosing)
            {
                UpdateCurrentLyricsLine();
            }
        }

        private void EnsureWindowOnTop()
        {
            if (_isClosing) return;

            // 如果全屏模式激活，不要强制显示窗口
            if (FullScreenDetector.IsFullScreenActive && ConfigManager.CurrentConfig.HideOnFullscreen)
            {
                return;
            }

            try
            {
                TaskbarMonitor.SetWindowToTaskbarLevel(this);

                if (this.Visibility != Visibility.Visible && !_isClosing)
                {
                    this.Visibility = Visibility.Visible;
                }

                if (this.WindowState != WindowState.Normal && !_isClosing)
                {
                    this.WindowState = WindowState.Normal;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"设置窗口置顶时出错: {ex.Message}");
            }
        }

        private void InitializeWindow()
        {
            this.WindowState = WindowState.Normal;
            this.ShowActivated = false;
            this.ShowInTaskbar = false;
            this.Topmost = true;

            this.Focusable = true;

            ApplyPositionOffset();
            SetWindowTransparency();
            TaskbarMonitor.SetWindowToTaskbarLevel(this);

            ApplyConfig();
        }

        private void SetWindowTransparency()
        {
            TaskbarMonitor.EnableMouseEvents(this);
        }

        public void RefreshConfig()
        {
            ApplyConfig();
            _forceRefresh = true;

            // 清理歌词渲染缓存，确保过滤规则立即生效（缓存机制已移至MainWindow）
            // _lastCachedVisual = null;
            // _lastCachedLyricsKey = "";

            // 应用位置偏移
            ApplyPositionOffset();

            // 根据配置重新设置全屏检测
            if (ConfigManager.CurrentConfig.HideOnFullscreen)
            {
                if (!FullScreenDetector.IsFullScreenActive)
                {
                    FullScreenDetector.Start();
                    Logger.Info("配置更新：全屏检测已启动");
                }
            }
            else
            {
                FullScreenDetector.Stop();
                Logger.Info("配置更新：全屏检测已停止");

                // 如果之前因为全屏而隐藏了窗口，现在要显示出来
                if (this.Visibility == Visibility.Collapsed)
                {
                    this.Visibility = Visibility.Visible;
                    TaskbarMonitor.ForceShowWindow(this);
                }
            }
        }

        public void ForceRefreshLyrics()
        {
            _forceRefresh = true;
            // 清空缓存以强制刷新
            _lastCachedVisual = null;
            _lastCachedLyricsKey = "";
        }

        private void ApplyConfig()
        {
            var config = ConfigManager.CurrentConfig;

            this.Background = Brushes.Transparent;
            LyricsContainer.Background = Brushes.Transparent;

            if (!string.IsNullOrEmpty(config.BackgroundColor) && 
                config.BackgroundColor != "#00000000")
            {
                try
                {
                    var brush = (Brush)new BrushConverter().ConvertFromString(config.BackgroundColor);
                    LyricsContainer.Background = brush;
                }
                catch (Exception ex)
                {
                    Logger.Error($"应用背景颜色时出错: {ex.Message}");
                    LyricsContainer.Background = Brushes.Transparent;
                }
            }

            // 应用歌词宽度限制
            LyricsContainer.MaxWidth = config.LyricsWidth;

            ApplyAlignment();
        }

        private void ApplyAlignment()
        {
            var config = ConfigManager.CurrentConfig;
            if (string.IsNullOrEmpty(config.Alignment))
                return;

            HorizontalAlignment alignment = HorizontalAlignment.Center;
            Thickness margin = new Thickness(0);

            switch (config.Alignment.ToLower())
            {
                case "left":
                    alignment = HorizontalAlignment.Left;
                    break;
                case "right":
                    alignment = HorizontalAlignment.Right;
                    margin = new Thickness(0, 0, this.ActualWidth * 0.25, 0);
                    break;
                case "center":
                default:
                    alignment = HorizontalAlignment.Center;
                    break;
            }

            LyricsContainer.HorizontalAlignment = alignment;
            ControlPanelBorder.HorizontalAlignment = alignment;

            LyricsContainer.Margin = margin;
            ControlPanelBorder.Margin = margin;

            // 移除频繁的对齐方式日志输出
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            _forceRefresh = true;
            
            ApplyAlignment();
        }

        /// <summary>
        /// 更新歌词
        /// 当歌曲变化时被调用，从API获取并更新歌词
        /// </summary>
        private async Task UpdateLyrics()
        {
            if (_isClosing) return;

            try
            {
                var lyricsResponse = await _apiService.GetLyricsAsync();
                if (lyricsResponse?.Status != "success" || string.IsNullOrEmpty(lyricsResponse.Lyric))
                {
                    // 没有歌词时不清空显示，保持当前歌词
                    _forceRefresh = false;
                    return;
                }

                string currentLyrics = lyricsResponse.Lyric.Trim();

                if (currentLyrics == _lastLyricsText && !_forceRefresh)
                {
                    return; // 歌词没有变化，跳过解析
                }

                _lastLyricsText = currentLyrics;
                _forceRefresh = false;

                // 清空缓存，因为歌词已经变化
                _lastCachedLyricsKey = "";
                _lastCachedVisual = null;

                // 只在歌词真正变化时才执行ParseLyrics，并传递歌曲标题
                _lyricsLines = LyricsRenderer.ParseLyrics(currentLyrics, _lastSongTitle);

                // 只在歌词行数变化时记录日志
                if (_lyricsLines.Count != _lastLyricsLineCount)
                {
                    Logger.Info($"已加载 {_lyricsLines.Count} 行歌词");
                    _lastLyricsLineCount = _lyricsLines.Count;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"更新歌词时出错: {ex.Message}");
                _forceRefresh = false;
            }
        }

        private void UpdateCurrentLyricsLine()
        {
            if (_isClosing)
            {
                return;
            }

            // 如果有歌词数据，尝试更新显示
            if (_lyricsLines != null && _lyricsLines.Count > 0)
            {
                var currentLine = LyricsRenderer.GetCurrentLyricsLine(
                    _lyricsLines,
                    _currentPosition
                );
                if (currentLine != null)
                {
                    // 检查歌词行是否改变
                    bool lyricsLineChanged =
                        _lastLyricsLine?.OriginalText != currentLine.OriginalText;

                    // 创建缓存键（基于歌词文本和位置）
                    string cacheKey = $"{currentLine.OriginalText}_{_currentPosition:F0}";

                    var config = ConfigManager.CurrentConfig;

                    // 检查缓存
                    FrameworkElement lyricsVisual;
                    if (cacheKey == _lastCachedLyricsKey && _lastCachedVisual != null)
                    {
                        // 使用缓存
                        lyricsVisual = _lastCachedVisual;
                    }
                    else
                    {
                        // 创建新的视觉对象并缓存
                        lyricsVisual = LyricsRenderer.CreateDualLineLyricsVisual(
                            currentLine, config, ActualWidth, _currentPosition);
                        _lastCachedVisual = lyricsVisual;
                        _lastCachedLyricsKey = cacheKey;
                    }

                    // 只有当歌词行真正改变时才更新内容
                    if (lyricsLineChanged)
                    {
                        // 更新歌词内容
                        if (LyricsContent.Content != lyricsVisual)
                        {
                            LyricsContent.Content = lyricsVisual;
                        }

                        _lastLyricsLine = currentLine;
                    }
                    else
                    {
                        // 歌词行未改变，只检查是否需要更新内容
                        if (LyricsContent.Content != lyricsVisual)
                        {
                            LyricsContent.Content = lyricsVisual;
                        }
                    }
                }
                // 没有匹配的歌词行时，不清空显示，保持当前歌词
            }
            // 没有歌词数据时，也保持当前显示，不清空
        }

        
        private void ClearLyrics()
        {
            if (!_isClosing)
            {
                LyricsContent.Content = null;
            }
            _lyricsLines.Clear();
        }

        private void ApplyPositionOffset()
        {
            var config = ConfigManager.CurrentConfig;
            TaskbarMonitor.PositionWindowOnTaskbar(this, config.PositionOffsetX, config.PositionOffsetY);
        }

        
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            TaskbarMonitor.SetWindowToTaskbarLevel(this);
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);

            if (this.WindowState != WindowState.Normal && !_isClosing)
            {
                this.WindowState = WindowState.Normal;
                TaskbarMonitor.ForceShowWindow(this);
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && !_isClosing)
                this.DragMove();
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!_isClosing)
                ShowControlPanel();
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isClosing)
            {
                _isMouseOver = false;
                _mouseLeaveTimer.Start();
            }
        }

        private void LyricsContainer_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!_isClosing)
                ShowControlPanel();
        }

        private void LyricsContainer_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isClosing)
            {
                _isMouseOver = false;
                _mouseLeaveTimer.Start();
            }
        }

        private void ControlPanelBorder_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!_isClosing)
            {
                _isMouseOver = true;
                _mouseLeaveTimer.Stop();
            }
        }

        private void ControlPanelBorder_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isClosing)
            {
                _isMouseOver = false;
                _mouseLeaveTimer.Start();
            }
        }

        private void ShowControlPanel()
        {
            if (_isClosing) return;

            _isMouseOver = true;
            _mouseLeaveTimer.Stop();

            ControlPanelBorder.Visibility = Visibility.Visible;
            LyricsContent.Visibility = Visibility.Collapsed;
        }

        private void HideControlPanel()
        {
            if (_isClosing) return;

            ControlPanelBorder.Visibility = Visibility.Collapsed;
            LyricsContent.Visibility = Visibility.Visible;
        }

        private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            try
            {
                bool result = await _apiService.PlayPauseAsync();
            }
            catch (Exception ex)
            {
                Logger.Error($"播放/暂停时出错: {ex.Message}");
            }
        }

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            try
            {
                bool result = await _apiService.NextTrackAsync();
            }
            catch (Exception ex)
            {
                Logger.Error($"下一曲时出错: {ex.Message}");
            }
        }

        private async void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;

            try
            {
                bool result = await _apiService.PreviousTrackAsync();
            }
            catch (Exception ex)
            {
                Logger.Error($"上一曲时出错: {ex.Message}");
            }
        }

        private void SetupFullScreenDetection()
        {
            FullScreenDetector.FullScreenStatusChanged += OnFullScreenStatusChanged;

            // 总是先启动检测，OnFullScreenStatusChanged 会根据配置决定是否响应
            FullScreenDetector.Start();
            Logger.Info($"全屏检测已启动，配置全屏时隐藏: {ConfigManager.CurrentConfig.HideOnFullscreen}");
        }

        private void OnFullScreenStatusChanged(object sender, bool isFullScreen)
        {
            if (_isClosing) return;

            Logger.Info($"全屏状态变化: {isFullScreen}, 配置全屏时隐藏: {ConfigManager.CurrentConfig.HideOnFullscreen}, 当前窗口可见性: {this.Visibility}");

            // 只在配置启用时响应全屏状态变化
            if (!ConfigManager.CurrentConfig.HideOnFullscreen)
            {
                Logger.Info("全屏隐藏功能已禁用，忽略状态变化");
                return;
            }

            try
            {
                if (isFullScreen)
                {
                    // 检测到全屏应用，隐藏歌词
                    if (this.Visibility == Visibility.Visible)
                    {
                        Logger.Info($"全屏应用检测到，隐藏歌词 - {FullScreenDetector.GetActiveWindowTitle()}");

                        // 使用多种方法确保窗口隐藏
                        this.Visibility = Visibility.Hidden;
                        this.Hide();

                        // 额外的Win32 API调用确保隐藏
                        try
                        {
                            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                            TaskbarMonitor.ShowWindow(hwnd, 0); // SW_HIDE = 0
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"使用Win32 API隐藏窗口失败: {ex.Message}");
                        }

                        Logger.Info($"窗口状态 - Visibility: {this.Visibility}, IsVisible: {this.IsVisible}");
                    }
                    else
                    {
                        Logger.Info($"窗口已经隐藏，当前状态 - Visibility: {this.Visibility}, IsVisible: {this.IsVisible}");
                    }
                }
                else
                {
                    // 退出全屏，恢复显示
                    if (this.Visibility != Visibility.Visible || !this.IsVisible)
                    {
                        Logger.Info("退出全屏，恢复歌词显示");

                        // 使用多种方法确保窗口显示
                        this.Visibility = Visibility.Visible;
                        this.Show();

                        // 强制显示并重新设置窗口属性
                        TaskbarMonitor.ForceShowWindow(this);

                        Logger.Info($"窗口恢复后状态 - Visibility: {this.Visibility}, IsVisible: {this.IsVisible}");
                    }
                    else
                    {
                        Logger.Info($"窗口已经可见，当前状态 - Visibility: {this.Visibility}, IsVisible: {this.IsVisible}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"处理全屏状态变化时出错: {ex.Message}");
            }
        }
    }
}