using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
        private DispatcherTimer _configTimer;
        private DispatcherTimer _restoreTimer;
        private LyricsConfig _currentConfig;
        private string _lastLyricText = "";
        private bool _forceRefresh = false;

        public MainWindow()
        {
            InitializeComponent();
            
            _apiService = new LyricsApiService();
            _currentConfig = new LyricsConfig();

            this.IsVisibleChanged += MainWindow_IsVisibleChanged;

            InitializeWindow();
            SetupTimers();
        }

        private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!this.IsVisible)
            {
                this.Visibility = Visibility.Visible;
                TaskbarMonitor.ForceShowWindow(this);
                Debug.WriteLine("On");
            }
        }

        private void SetupTimers()
        {
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _updateTimer.Tick += async (s, e) => await UpdateLyrics();
            _updateTimer.Start();

            _positionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _positionTimer.Tick += (s, e) => UpdateWindowPosition();
            _positionTimer.Start();

            _configTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _configTimer.Tick += async (s, e) => await LoadConfig();
            _configTimer.Start();

            _restoreTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1)
            };
            _restoreTimer.Tick += (s, e) => EnsureWindowOnTop();
            _restoreTimer.Start();
        }

        private void EnsureWindowOnTop()
        {
            try
            {
                TaskbarMonitor.SetWindowToTaskbarLevel(this);
                
                if (this.Visibility != Visibility.Visible)
                {
                    this.Visibility = Visibility.Visible;
                    Debug.WriteLine("On");
                }
                
                if (this.WindowState != WindowState.Normal)
                {
                    this.WindowState = WindowState.Normal;
                    Debug.WriteLine("Fixed");
                }
                
                if (!this.IsActive)
                {
                    TaskbarMonitor.ForceShowWindow(this);
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
            
            TaskbarMonitor.PositionWindowOnTaskbar(this);
            TaskbarMonitor.EnableWindowTransparency(this);
            TaskbarMonitor.SetWindowToTaskbarLevel(this);
            
            _ = LoadConfig();
        }

        private async Task LoadConfig()
        {
            try
            {
                var configResponse = await _apiService.GetConfigAsync();
                if (configResponse?.Status == "success" && configResponse.Config != null)
                {
                    bool configChanged = HasConfigChanged(configResponse.Config);
                    
                    if (configChanged)
                    {
                        ProcessConfig(configResponse.Config);
                        ApplyConfig();
                        
                        Debug.WriteLine($"Updated: Font={_currentConfig.FontFamily}, Size={_currentConfig.FontSize}, Color={_currentConfig.FontColor}, Align={_currentConfig.Alignment}, Show={_currentConfig.ShowTranslation}");
                        
                        _forceRefresh = true;
                        _ = UpdateLyrics();
                    }
                }
                else
                {
                    Debug.WriteLine("Default");
                    ApplyConfig();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error: {ex.Message}");
                ApplyConfig();
            }
        }

        private bool HasConfigChanged(LyricsConfig newConfig)
        {
            return _currentConfig.FontFamily != newConfig.FontFamily ||
                   _currentConfig.FontSize != newConfig.FontSize ||
                   _currentConfig.FontColor != newConfig.FontColor ||
                   _currentConfig.BackgroundColor != newConfig.BackgroundColor ||
                   _currentConfig.Alignment != newConfig.Alignment ||
                   _currentConfig.ShowTranslation != newConfig.ShowTranslation ||
                   _currentConfig.TranslationFontSize != newConfig.TranslationFontSize ||
                   _currentConfig.TranslationFontColor != newConfig.TranslationFontColor;
        }

        private void ProcessConfig(LyricsConfig newConfig)
        {
            if (!string.IsNullOrEmpty(newConfig.FontFamily))
                _currentConfig.FontFamily = newConfig.FontFamily;

            if (newConfig.FontSize > 0)
                _currentConfig.FontSize = newConfig.FontSize;

            if (!string.IsNullOrEmpty(newConfig.FontColor))
                _currentConfig.FontColor = newConfig.FontColor;

            if (!string.IsNullOrEmpty(newConfig.BackgroundColor))
                _currentConfig.BackgroundColor = newConfig.BackgroundColor;

            if (!string.IsNullOrEmpty(newConfig.Alignment))
                _currentConfig.Alignment = newConfig.Alignment;

            _currentConfig.ShowTranslation = newConfig.ShowTranslation;

            if (newConfig.TranslationFontSize > 0)
                _currentConfig.TranslationFontSize = newConfig.TranslationFontSize;
            else
                _currentConfig.TranslationFontSize = Math.Max(_currentConfig.FontSize - 2, 8);

            if (!string.IsNullOrEmpty(newConfig.TranslationFontColor))
                _currentConfig.TranslationFontColor = newConfig.TranslationFontColor;
            else
                _currentConfig.TranslationFontColor = _currentConfig.FontColor; 
        }

        private void ApplyConfig()
        {
            this.Background = Brushes.Transparent;
            LyricsContainer.Background = Brushes.Transparent;

            if (!string.IsNullOrEmpty(_currentConfig.BackgroundColor) && 
                _currentConfig.BackgroundColor != "#00000000")
            {
                try
                {
                    var brush = (Brush)new BrushConverter().ConvertFromString(_currentConfig.BackgroundColor);
                    LyricsContainer.Background = brush;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error: {ex.Message}");
                    LyricsContainer.Background = Brushes.Transparent;
                }
            }
        }

        private async Task UpdateLyrics()
        {
            try
            {
                var lyricsResponse = await _apiService.GetLyricsAsync();
                if (lyricsResponse?.Status != "success" || string.IsNullOrEmpty(lyricsResponse.Lyric))
                {
                    if (!string.IsNullOrEmpty(_lastLyricText) || _forceRefresh)
                    {
                        ClearLyrics();
                        _lastLyricText = "";
                        _forceRefresh = false;
                    }
                    return;
                }

                string currentLyric = lyricsResponse.Lyric.Trim();
                
                if (currentLyric == _lastLyricText && !_forceRefresh)
                    return;

                _lastLyricText = currentLyric;
                _forceRefresh = false;

                var lyricsLine = LyricsRenderer.ParseLyricsLine(currentLyric);
                var lyricsVisual = LyricsRenderer.CreateDualLineLyricsVisual(lyricsLine, _currentConfig, ActualWidth);

                LyricsContent.Content = null;
                LyricsContent.Content = lyricsVisual;
                
                Debug.WriteLine($"Show: OriginalText='{lyricsLine.OriginalText}', TransactionText='{lyricsLine.TranslationText}'");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error: {ex.Message}");
                _forceRefresh = false;
            }
        }

        private void ClearLyrics()
        {
            LyricsContent.Content = null;
            _lastLyricText = "";
        }

        private void UpdateWindowPosition()
        {
            TaskbarMonitor.PositionWindowOnTaskbar(this);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            TaskbarMonitor.SetWindowToTaskbarLevel(this);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            _forceRefresh = true;
            _ = UpdateLyrics();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            
            if (this.WindowState != WindowState.Normal)
            {
                this.WindowState = WindowState.Normal;
                TaskbarMonitor.ForceShowWindow(this);
                Debug.WriteLine("On");
            }
        }

        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                this.DragMove();
        }
    }
}