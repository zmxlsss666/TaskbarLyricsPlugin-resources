using System;
using System.Drawing;
using System.Windows;
using System.Diagnostics;

namespace TaskbarLyrics
{
    /// <summary>
    /// 应用程序主类
    /// 负责：
    /// 1. 初始化配置和日志系统
    /// 2. 管理系统托盘图标和右键菜单
    /// 3. 处理全局异常
    /// 4. 管理设置窗口和关于窗口
    /// 5. 提供快捷设置功能（字体、颜色、对齐等）
    /// </summary>
    public partial class App : Application
    {
        #region 私有字段

        // 系统托盘相关
        private System.Windows.Forms.NotifyIcon _notifyIcon;  // 系统托盘图标

        // 窗口管理
        private SettingsWindow _settingsWindow;              // 设置窗口实例
        private AboutWindow _aboutWindow;                    // 关于窗口实例

        // 右键菜单项
        private System.Windows.Forms.ToolStripMenuItem _toggleTranslationItem; // 显示/隐藏翻译菜单项

        #endregion

        #region 应用程序生命周期

        /// <summary>
        /// 应用程序启动事件处理程序
        /// 执行必要的初始化操作
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 初始化配置管理器，加载用户设置
            ConfigManager.Initialize();

            // 应用日志级别配置
            // 必须在配置初始化后执行，以便读取用户的日志设置
            ApplyLogLevelConfig();

            // 创建系统托盘图标和右键菜单
            CreateTrayIcon();

            // 注册全局异常处理程序
            // 捕获未处理的异常，记录日志并显示友好提示
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                Logger.Error($"未处理的异常: {args.ExceptionObject}");
                MessageBox.Show($"Error: {args.ExceptionObject}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            };
        }

        /// <summary>
        /// 应用程序退出事件处理程序
        /// 清理资源
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                // 释放托盘图标资源
                _notifyIcon?.Dispose();
                base.OnExit(e);
            }
            catch
            {
                // 忽略退出时的异常，确保程序能够正常退出
            }
        }

        #endregion

        private void CreateTrayIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            
            _notifyIcon.Icon = SystemIcons.Application;
            _notifyIcon.Text = "任务栏歌词";
            _notifyIcon.Visible = true;
            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            
            var settingsMenuItem = new System.Windows.Forms.ToolStripMenuItem("设置");
            settingsMenuItem.Click += (s, e) => ShowSettingsWindow();
            contextMenu.Items.Add(settingsMenuItem);

            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var fontMenuItem = new System.Windows.Forms.ToolStripMenuItem("字体设置");
            var fontFamilyItem = new System.Windows.Forms.ToolStripMenuItem("字体");
            var fontSizeItem = new System.Windows.Forms.ToolStripMenuItem("字体大小");
            var fontColorItem = new System.Windows.Forms.ToolStripMenuItem("字体颜色");
            
            fontMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                fontFamilyItem, fontSizeItem, fontColorItem
            });
            contextMenu.Items.Add(fontMenuItem);

            var highlightColorItem = new System.Windows.Forms.ToolStripMenuItem("高亮颜色");
            highlightColorItem.Click += (s, e) => ShowColorPicker("highlight");
            contextMenu.Items.Add(highlightColorItem);

            var alignmentMenuItem = new System.Windows.Forms.ToolStripMenuItem("对齐方式");
            var leftAlignItem = new System.Windows.Forms.ToolStripMenuItem("左对齐");
            var centerAlignItem = new System.Windows.Forms.ToolStripMenuItem("居中");
            var rightAlignItem = new System.Windows.Forms.ToolStripMenuItem("右对齐");
            
            leftAlignItem.Click += (s, e) => SetAlignment("left");
            centerAlignItem.Click += (s, e) => SetAlignment("center");
            rightAlignItem.Click += (s, e) => SetAlignment("right");
            
            alignmentMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                leftAlignItem, centerAlignItem, rightAlignItem
            });
            contextMenu.Items.Add(alignmentMenuItem);

            var translationMenuItem = new System.Windows.Forms.ToolStripMenuItem("翻译设置");
            
            _toggleTranslationItem = new System.Windows.Forms.ToolStripMenuItem("显示翻译");
            _toggleTranslationItem.Click += (s, e) => ToggleTranslation();
            UpdateTranslationMenuItem();
            
            translationMenuItem.DropDownItems.Add(_toggleTranslationItem);
            contextMenu.Items.Add(translationMenuItem);

            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var aboutMenuItem = new System.Windows.Forms.ToolStripMenuItem("关于");
            aboutMenuItem.Click += (s, e) => ShowAboutWindow();
            contextMenu.Items.Add(aboutMenuItem);

            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var exitMenuItem = new System.Windows.Forms.ToolStripMenuItem("退出");
            exitMenuItem.Click += (s, e) => ShutdownApplication();
            contextMenu.Items.Add(exitMenuItem);

            _notifyIcon.ContextMenuStrip = contextMenu;

            _notifyIcon.DoubleClick += (s, e) => ToggleMainWindow();

            InitializeFontMenu(fontFamilyItem, fontSizeItem, fontColorItem);
        }

        
        private void InitializeFontMenu(System.Windows.Forms.ToolStripMenuItem fontFamilyItem, System.Windows.Forms.ToolStripMenuItem fontSizeItem, System.Windows.Forms.ToolStripMenuItem fontColorItem)
        {
            var fontFamilies = new[] { "MiSans", "Microsoft YaHei", "SimHei", "SimSun", "Arial", "Segoe UI" };
            foreach (var fontFamily in fontFamilies)
            {
                var item = new System.Windows.Forms.ToolStripMenuItem(fontFamily);
                item.Click += (s, e) => SetFontFamily(fontFamily);
                fontFamilyItem.DropDownItems.Add(item);
            }

            var fontSizes = new[] { 12, 14, 16, 18, 20, 24, 28 };
            foreach (var size in fontSizes)
            {
                var item = new System.Windows.Forms.ToolStripMenuItem($"{size}px");
                item.Click += (s, e) => SetFontSize(size);
                fontSizeItem.DropDownItems.Add(item);
            }

            var colors = new[]
            {
                ("白色", "#FFFFFF"),
                ("红色", "#FF0000"),
                ("绿色", "#00FF00"),
                ("蓝色", "#0000FF"),
                ("黄色", "#FFFF00"),
                ("青色", "#00FFFF"),
                ("粉色", "#FF00FF")
            };
            
            foreach (var (name, color) in colors)
            {
                var item = new System.Windows.Forms.ToolStripMenuItem(name);
                item.Click += (s, e) => SetFontColor(color);
                fontColorItem.DropDownItems.Add(item);
            }
        }

        private void UpdateTranslationMenuItem()
        {
            if (_toggleTranslationItem != null)
            {
                bool showTranslation = ConfigManager.CurrentConfig.ShowTranslation;
                _toggleTranslationItem.Text = showTranslation ? "隐藏翻译" : "显示翻译";
                _toggleTranslationItem.Checked = showTranslation;
            }
        }

        private void ShowSettingsWindow()
        {
            if (_settingsWindow == null || !_settingsWindow.IsLoaded)
            {
                _settingsWindow = new SettingsWindow();
                _settingsWindow.Closed += (s, e) => 
                {
                    _settingsWindow = null;
                    UpdateTranslationMenuItem();
                };
            }
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }

        private void ShowAboutWindow()
        {
            if (_aboutWindow == null || !_aboutWindow.IsLoaded)
            {
                _aboutWindow = new AboutWindow();
                _aboutWindow.Closed += (s, e) => _aboutWindow = null;
            }
            _aboutWindow.Show();
            _aboutWindow.Activate();
        }

        private void ShowColorPicker(string type)
        {
            var colorDialog = new System.Windows.Forms.ColorDialog();
            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var color = colorDialog.Color;
                var colorString = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
                
                if (type == "highlight")
                {
                    SetHighlightColor(colorString);
                }
            }
        }

        private void SetFontFamily(string fontFamily)
        {
            ConfigManager.CurrentConfig.FontFamily = fontFamily;
            ConfigManager.SaveConfig();
            RefreshMainWindow();
        }

        private void SetFontSize(int size)
        {
            ConfigManager.CurrentConfig.FontSize = size;
            ConfigManager.SaveConfig();
            RefreshMainWindow();
        }

        private void SetFontColor(string color)
        {
            ConfigManager.CurrentConfig.FontColor = color;
            ConfigManager.SaveConfig();
            RefreshMainWindow();
        }

        private void SetHighlightColor(string color)
        {
            ConfigManager.CurrentConfig.HighlightColor = color;
            ConfigManager.SaveConfig();
            RefreshMainWindow();
        }

        private void SetAlignment(string alignment)
        {
            ConfigManager.CurrentConfig.Alignment = alignment;
            ConfigManager.SaveConfig();
            RefreshMainWindow();
        }

        private void ToggleTranslation()
        {
            ConfigManager.CurrentConfig.ShowTranslation = !ConfigManager.CurrentConfig.ShowTranslation;
            ConfigManager.SaveConfig();
            UpdateTranslationMenuItem();
            RefreshMainWindow();
        }

        private void ToggleMainWindow()
        {
            var mainWindow = MainWindow as MainWindow;
            if (mainWindow != null)
            {
                if (mainWindow.Visibility == Visibility.Visible)
                {
                    mainWindow.Hide();
                }
                else
                {
                    mainWindow.Show();
                    mainWindow.WindowState = WindowState.Normal;
                    mainWindow.Activate();
                }
            }
        }

        private void RefreshMainWindow()
        {
            var mainWindow = MainWindow as MainWindow;
            mainWindow?.RefreshConfig();
        }

        private void ShutdownApplication()
        {
            try
            {
                _notifyIcon.Visible = false;
                
                _settingsWindow?.Close();
                _aboutWindow?.Close();
                
                ConfigManager.SaveConfig();
                
                var mainWindow = MainWindow as MainWindow;
                mainWindow?.Close();
                
                Shutdown();
            }
            catch (Exception ex)
            {
                Logger.Error($"关闭应用程序时出错: {ex.Message}");
                Environment.Exit(0);
            }
        }

        private void ApplyLogLevelConfig()
        {
            try
            {
                var config = ConfigManager.CurrentConfig;
                string logLevelConfig = config?.LogLevel?.ToLower() ?? "auto";

                LogLevel targetLogLevel;

                switch (logLevelConfig)
                {
                    case "off":
                        targetLogLevel = LogLevel.Off;
                        break;
                    case "error":
                        targetLogLevel = LogLevel.Error;
                        break;
                    case "info":
                        targetLogLevel = LogLevel.Info;
                        break;
                    case "debug":
                        targetLogLevel = LogLevel.Debug;
                        break;
                    case "auto":
                    default:
                        // 使用自动检测的逻辑（已在Logger静态构造函数中处理）
                        return;
                }

                Logger.SetLogLevel(targetLogLevel);
            }
            catch (Exception ex)
            {
                // 如果设置日志级别失败，继续使用默认级别
                System.Diagnostics.Debug.WriteLine($"设置日志级别失败: {ex.Message}");
            }
        }
    }
}

