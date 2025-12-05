using System;
using System.IO;
using System.Diagnostics;

namespace TaskbarLyrics
{
    /// <summary>
    /// 日志级别枚举
    /// 定义不同的日志输出级别
    /// </summary>
    public enum LogLevel
    {
        Off = 0,      // 关闭日志输出
        Error = 1,    // 仅输出错误信息
        Info = 2,     // 输出一般信息和错误
        Debug = 3     // 输出所有信息（包括调试信息）
    }

    /// <summary>
    /// 日志记录器类
    /// 负责：
    /// 1. 提供分级日志记录功能
    /// 2. 自动过滤重复日志消息
    /// 3. 根据环境自动设置日志级别
    /// 4. 将日志写入到用户AppData目录
    /// 5. 支持运行时动态调整日志级别
    /// </summary>
    public static class Logger
    {
        #region 私有字段

        /// <summary>
        /// 日志文件路径
        /// 存储在 %AppData%\TaskbarLyrics\debug.log
        /// </summary>
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskbarLyrics", "debug.log");

        /// <summary>
        /// 线程锁，确保多线程环境下日志写入的线程安全
        /// </summary>
        private static readonly object _lock = new object();

        /// <summary>
        /// 上次记录的消息内容
        /// 用于检测重复消息
        /// </summary>
        private static string _lastMessage = string.Empty;

        /// <summary>
        /// 上次记录消息的时间
        /// 用于检测重复消息
        /// </summary>
        private static DateTime _lastMessageTime = DateTime.MinValue;

        /// <summary>
        /// 重复消息过滤阈值
        /// 5秒内的重复消息会被过滤，避免日志泛滥
        /// </summary>
        private static readonly TimeSpan _duplicateThreshold = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 当前日志级别
        /// 默认关闭，仅在调试时开启
        /// </summary>
        private static LogLevel _currentLogLevel = LogLevel.Off;

        #endregion

        #region 静态构造函数

        /// <summary>
        /// 静态构造函数
        /// 初始化日志系统
        /// </summary>
        static Logger()
        {
            // 根据运行环境设置默认日志级别
            SetDefaultLogLevel();

            // 确保日志目录存在
            var directory = Path.GetDirectoryName(LogPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 只有在日志级别不为Off时才创建日志文件
            if (_currentLogLevel != LogLevel.Off)
            {
                try
                {
                    // 写入启动标记
                    File.WriteAllText(LogPath, $"=== TaskbarLyrics 启动 - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
                    File.AppendAllText(LogPath, $"=== 日志级别: {_currentLogLevel} ==={Environment.NewLine}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"无法创建日志文件: {ex.Message}");
                }
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 设置默认日志级别
        /// 根据编译模式和运行环境自动选择合适的日志级别
        /// </summary>
        private static void SetDefaultLogLevel()
        {
#if DEBUG
            // 开发环境默认使用Debug级别
            _currentLogLevel = LogLevel.Debug;
#else
            // 生产环境检查是否附加了调试器
            if (Debugger.IsAttached)
            {
                // 如果附加了调试器，使用Debug级别
                _currentLogLevel = LogLevel.Debug;
            }
            else
            {
                // 否则默认关闭日志
                _currentLogLevel = LogLevel.Off;
            }
#endif

            // 也可以通过环境变量覆盖日志级别
            // 环境变量：TASKBARDLYRICS_LOG_LEVEL=Debug
            string envLogLevel = Environment.GetEnvironmentVariable("TASKBARDLYRICS_LOG_LEVEL");
            if (!string.IsNullOrEmpty(envLogLevel) && Enum.TryParse<LogLevel>(envLogLevel, true, out LogLevel logLevel))
            {
                _currentLogLevel = logLevel;
            }
        }

        /// <summary>
        /// 写入日志到文件
        /// 包含重复消息过滤逻辑
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="message">日志消息</param>
        private static void WriteLog(string level, string message)
        {
            // 如果日志级别为Off，不写入任何日志
            if (_currentLogLevel == LogLevel.Off)
                return;

            try
            {
                lock (_lock)
                {
                    // 过滤重复的消息
                    if (message == _lastMessage && DateTime.Now - _lastMessageTime < _duplicateThreshold)
                    {
                        return; // 跳过重复的消息
                    }

                    // 更新重复消息检测信息
                    _lastMessage = message;
                    _lastMessageTime = DateTime.Now;

                    // 格式化日志条目
                    var logEntry = $"[{DateTime.Now:HH:mm:ss}] {level}: {message}{Environment.NewLine}";

                    // 追加到日志文件
                    File.AppendAllText(LogPath, logEntry);
                }
            }
            catch (Exception ex)
            {
                // 日志写入失败时输出到控制台
                Console.WriteLine($"写入日志失败: {ex.Message}");
            }
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 设置日志级别
        /// 可以在运行时动态调整
        /// </summary>
        /// <param name="level">要设置的日志级别</param>
        public static void SetLogLevel(LogLevel level)
        {
            _currentLogLevel = level;

            if (level != LogLevel.Off)
            {
                try
                {
                    // 记录日志级别变更
                    File.AppendAllText(LogPath, $"=== 日志级别已更改为: {_currentLogLevel} - {DateTime.Now:HH:mm:ss} ==={Environment.NewLine}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"无法写入日志级别变更: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 获取当前日志级别
        /// </summary>
        /// <returns>当前日志级别</returns>
        public static LogLevel GetLogLevel()
        {
            return _currentLogLevel;
        }

        /// <summary>
        /// 记录信息级别日志
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void Info(string message)
        {
            if (_currentLogLevel >= LogLevel.Info)
                WriteLog("INFO", message);
        }

        /// <summary>
        /// 记录错误级别日志
        /// </summary>
        /// <param name="message">错误消息</param>
        public static void Error(string message)
        {
            if (_currentLogLevel >= LogLevel.Error)
                WriteLog("ERROR", message);
        }

        /// <summary>
        /// 记录调试级别日志
        /// </summary>
        /// <param name="message">调试消息</param>
        public static void Debug(string message)
        {
            if (_currentLogLevel >= LogLevel.Debug)
                WriteLog("DEBUG", message);
        }

        /// <summary>
        /// 获取日志文件路径
        /// </summary>
        /// <returns>日志文件的完整路径</returns>
        public static string GetLogPath()
        {
            return LogPath;
        }

        #endregion
    }
}