using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using TaskbarLyrics.Models;

namespace TaskbarLyrics
{
    /// <summary>
    /// 配置管理器类
    /// 负责应用程序配置的加载、保存和管理
    /// 配置文件存储在用户的AppData目录下
    /// </summary>
    public static class ConfigManager
    {
        #region 私有字段

        /// <summary>
        /// 配置文件路径
        /// 存储在 %AppData%\TaskbarLyrics\config.json
        /// </summary>
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskbarLyrics", "config.json");

        #endregion

        #region 公共属性

        /// <summary>
        /// 当前配置对象
        /// 包含应用程序的所有用户设置
        /// </summary>
        public static LyricsConfig CurrentConfig { get; private set; }

        #endregion

        #region 初始化方法

        /// <summary>
        /// 初始化配置管理器
        /// 在应用程序启动时调用，加载配置文件
        /// </summary>
        public static void Initialize()
        {
            LoadConfig();
        }

        #endregion

        #region 配置加载

        /// <summary>
        /// 从文件加载配置
        /// 如果配置文件不存在，则创建默认配置
        /// </summary>
        public static void LoadConfig()
        {
            try
            {
                // 检查配置文件是否存在
                if (File.Exists(ConfigPath))
                {
                    // 读取并反序列化配置文件
                    var json = File.ReadAllText(ConfigPath);
                    CurrentConfig = JsonSerializer.Deserialize<LyricsConfig>(json);
                }
                else
                {
                    // 配置文件不存在，创建默认配置
                    CurrentConfig = new LyricsConfig();
                    SaveConfig(); // 保存默认配置到文件
                }
            }
            catch (Exception ex)
            {
                // 加载失败时显示错误消息
                MessageBox.Show($"加载配置失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                // 使用默认配置
                CurrentConfig = new LyricsConfig();
            }
        }

        #endregion

        #region 配置保存

        /// <summary>
        /// 保存当前配置到文件
        /// 自动创建目录（如果不存在）
        /// </summary>
        public static void SaveConfig()
        {
            try
            {
                // 确保配置目录存在
                var directory = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 序列化配置为JSON（格式化输出）
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(CurrentConfig, options);

                // 写入文件
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                // 保存失败时显示错误消息
                MessageBox.Show($"保存配置失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion
    }
}