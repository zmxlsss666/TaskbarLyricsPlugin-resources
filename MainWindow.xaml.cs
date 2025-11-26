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
    public partial class MainWindow : Window
    {
        private LyricsApiService _apiService;
        private DispatcherTimer _updateTimer;
        private DispatcherTimer _positionTimer;
        private DispatcherTimer _restoreTimer;
        private DispatcherTimer _nowPlayingTimer;
        private List<LyricsLine> _lyricsLines = new List<LyricsLine>();
        private string _lastLyricsText = "";
        private bool _forceRefresh = false;
        private int _currentPosition = 0;
        private bool _isPlaying = false;
        private bool _isMouseOver = false;
        private DispatcherTimer _mouseLeaveTimer;
        private DispatcherTimer _smoothUpdateTimer;
        private bool _isClosing = false;

        public MainWindow()
        {
            InitializeComponent();
            
            _apiService = new LyricsApiService();

            this.IsVisibleChanged += MainWindow_IsVisibleChanged;
            this.Closing += MainWindow_Closing;

            InitializeWindow();
            SetupTimers();
            
            this.Focusable = true;
            this.IsHitTestVisible = true;
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _isClosing = true;
            
            _updateTimer?.Stop();
            _positionTimer?.Stop();
            _restoreTimer?.Stop();
            _nowPlayingTimer?.Stop();
            _smoothUpdateTimer?.Stop();
            _mouseLeaveTimer?.Stop();
        }

        private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_isClosing)
                return;

            if (!this.IsVisible)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_isClosing && !this.IsVisible)
                    {
                        this.Visibility = Visibility.Visible;
                        TaskbarMonitor.ForceShowWindow(this);
                        Debug.WriteLine("Window visibility restored");
                    }
                }), DispatcherPriority.Background);
            }
        }

        private void SetupTimers()
        {
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800)
            };
            _updateTimer.Tick += async (s, e) => await UpdateLyrics();
            _updateTimer.Start();

            _positionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _positionTimer.Tick += (s, e) => UpdateWindowPosition();
            _positionTimer.Start();

            _restoreTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _restoreTimer.Tick += (s, e) => EnsureWindowOnTop();
            _restoreTimer.Start();

            _nowPlayingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _nowPlayingTimer.Tick += async (s, e) => await UpdateNowPlaying();
            _nowPlayingTimer.Start();

            _smoothUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(32)
            };
            _smoothUpdateTimer.Tick += (s, e) => SmoothUpdateLyrics();
            _smoothUpdateTimer.Start();

            _mouseLeaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _mouseLeaveTimer.Tick += (s, e) =>
            {
                _mouseLeaveTimer.Stop();
                if (!_isMouseOver)
                {
                    HideControlPanel();
                }
            };
        }

        private async Task UpdateNowPlaying()
        {
            try
            {
                var nowPlaying = await _apiService.GetNowPlayingAsync();
                if (nowPlaying?.Status == "success")
                {
                    _currentPosition = nowPlaying.Position;
                    _isPlaying = nowPlaying.IsPlaying;
                    
                    PlayPauseButton.Content = _isPlaying ? "⏸" : "▶";
                    _forceRefresh = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"NowPlaying Error: {ex.Message}");
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
                Debug.WriteLine($"Error: {ex.Message}");
            }
        }

        private void InitializeWindow()
        {
            this.WindowState = WindowState.Normal;
            this.ShowActivated = false;
            this.ShowInTaskbar = false;
            this.Topmost = true;
            
            this.Focusable = true;
            
            TaskbarMonitor.PositionWindowOnTaskbar(this);
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
        }

        public void ForceRefreshLyrics()
        {
            _forceRefresh = true;
            LyricsRenderer.ClearCache();
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
                    Debug.WriteLine($"Error applying background color: {ex.Message}");
                    LyricsContainer.Background = Brushes.Transparent;
                }
            }

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
            
            Debug.WriteLine($"Alignment applied: {config.Alignment}, Margin: {margin}");
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            _forceRefresh = true;
            
            ApplyAlignment();
        }

        private async Task UpdateLyrics()
        {
            if (_isClosing) return;

            try
            {
                var lyricsResponse = await _apiService.GetLyricsAsync();
                if (lyricsResponse?.Status != "success" || string.IsNullOrEmpty(lyricsResponse.Lyric))
                {
                    if (_lyricsLines.Count > 0 || _forceRefresh)
                    {
                        ClearLyrics();
                        _lastLyricsText = "";
                        _forceRefresh = false;
                    }
                    return;
                }

                string currentLyrics = lyricsResponse.Lyric.Trim();
                
                if (currentLyrics == _lastLyricsText && !_forceRefresh)
                {
                    return;
                }

                _lastLyricsText = currentLyrics;
                _forceRefresh = false;

                _lyricsLines = LyricsRenderer.ParseLyrics(currentLyrics);
                
                Debug.WriteLine($"Parsed {_lyricsLines.Count} lyrics lines");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating lyrics: {ex.Message}");
                _forceRefresh = false;
            }
        }

        private void UpdateCurrentLyricsLine()
        {
            if (_lyricsLines == null || _lyricsLines.Count == 0 || _isClosing)
            {
                ClearLyrics();
                return;
            }

            var currentLine = LyricsRenderer.GetCurrentLyricsLine(_lyricsLines, _currentPosition);
            if (currentLine != null)
            {
                var config = ConfigManager.CurrentConfig;
                var lyricsVisual = LyricsRenderer.CreateDualLineLyricsVisual(currentLine, config, ActualWidth, _currentPosition);

                if (LyricsContent.Content != lyricsVisual)
                {
                    LyricsContent.Content = lyricsVisual;
                }
            }
            else
            {
                ClearLyrics();
            }
        }

        private void ClearLyrics()
        {
            if (!_isClosing)
            {
                LyricsContent.Content = null;
            }
            _lyricsLines.Clear();
        }

        private void UpdateWindowPosition()
        {
            if (_isClosing) return;
            TaskbarMonitor.PositionWindowOnTaskbar(this);
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
                Debug.WriteLine($"Error in Play/Pause: {ex.Message}");
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
                Debug.WriteLine($"Error in Next Track: {ex.Message}");
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
                Debug.WriteLine($"Error in Previous Track: {ex.Message}");
            }
        }
    }
}