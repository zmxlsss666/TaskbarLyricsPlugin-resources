using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TaskbarLyrics.Models;

namespace TaskbarLyrics
{
    /// <summary>
    /// 歌词渲染器类
    /// 负责：
    /// 1. 解析LRC格式的歌词文本
    /// 2. 生成逐字同步的时间轴
    /// 3. 创建歌词的视觉呈现
    /// 4. 实现歌词高亮动画效果
    /// 5. 支持双语显示（原文+翻译）
    /// 6. 过滤非歌词内容
    /// </summary>
    public class LyricsRenderer
    {
        #region 私有常量和字段

        /// <summary>
        /// 时间戳正则表达式模式
        /// 匹配格式：[分钟:秒.毫秒]，例如 [01:23.45]
        /// </summary>
        private const string TimeStampPattern = @"\[(\d+):(\d+)\.(\d+)\]";
        private static readonly Regex TimeStampRegex = new Regex(TimeStampPattern);

        // 动画相关
        private static DispatcherTimer _animationTimer;          // 动画定时器（60FPS）
        private static Dictionary<WordTiming, double> _wordProgressCache = new Dictionary<WordTiming, double>(); // 词语进度缓存
        private static int _lastPosition = -1;                   // 上次的播放位置

        // 过滤相关
        private static Regex _filterRegex = null;                // 歌词过滤正则表达式

        #endregion

        #region 静态构造函数

        /// <summary>
        /// 静态构造函数
        /// 初始化动画定时器
        /// </summary>
        static LyricsRenderer()
        {
            // 创建16ms间隔的定时器（约60FPS），实现流畅的动画效果
            _animationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _animationTimer.Tick += (s, e) => UpdateAnimations();
            _animationTimer.Start();
        }

        #endregion

        #region 歌词解析

        /// <summary>
        /// 解析歌词文本（重载方法，不带歌曲标题）
        /// </summary>
        /// <param name="lyricsText">歌词文本</param>
        /// <returns>解析后的歌词行列表</returns>
        public static List<LyricsLine> ParseLyrics(string lyricsText)
        {
            return ParseLyrics(lyricsText, null);
        }

        /// <summary>
        /// 解析歌词文本
        /// 处理LRC格式的歌词，支持双语显示和逐字同步
        /// </summary>
        /// <param name="lyricsText">歌词文本</param>
        /// <param name="songTitle">歌曲标题（可选）</param>
        /// <returns>解析后的歌词行列表</returns>
        public static List<LyricsLine> ParseLyrics(string lyricsText, string songTitle)
        {
            var lines = new List<LyricsLine>();

            // 检查歌词文本是否为空
            if (string.IsNullOrEmpty(lyricsText))
                return lines;

            // 按行分割歌词
            var lyricLines = lyricsText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

            // 按时间戳分组（处理同一时间有多行歌词的情况）
            var timeStampGroups = GroupLinesByTimeStamp(lyricLines);

            // 解析每个时间戳组的歌词
            foreach (var group in timeStampGroups)
            {
                var lyricsLine = ParseLyricsLineGroup(group.Key, group.Value);
                if (lyricsLine != null)
                {
                    lines.Add(lyricsLine);
                }
            }

            // 按开始时间排序
            lines.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            // 计算每行的结束时间（下一行的开始时间）
            for (int i = 0; i < lines.Count; i++)
            {
                if (i < lines.Count - 1)
                {
                    lines[i].EndTime = lines[i + 1].StartTime;
                }
                else
                {
                    // 最后一行默认持续5秒
                    lines[i].EndTime = lines[i].StartTime + 5000;
                }
            }

            // 如果有歌曲标题，将其作为歌词列表的第一项插入
            if (!string.IsNullOrEmpty(songTitle))
            {
                var titleLine = new LyricsLine
                {
                    OriginalText = songTitle,
                    StartTime = 0,
                    EndTime = lines.Count > 0 ? lines[0].StartTime : 3000,
                    IsWordTiming = false
                };

                lines.Insert(0, titleLine);

                // 重新计算所有歌词行的结束时间
                for (int i = 1; i < lines.Count - 1; i++)
                {
                    lines[i].EndTime = lines[i + 1].StartTime;
                }

                // 更新最后一行的结束时间
                if (lines.Count > 1)
                {
                    lines[lines.Count - 1].EndTime = lines[lines.Count - 1].StartTime + 5000;
                }
            }

            return lines;
        }

        /// <summary>
        /// 按时间戳对歌词行进行分组
        /// 处理同一时间有多行歌词的情况（如原文和翻译）
        /// </summary>
        /// <param name="lyricLines">歌词行数组</param>
        /// <returns>时间戳到歌词行列表的映射</returns>
        private static Dictionary<int, List<string>> GroupLinesByTimeStamp(string[] lyricLines)
        {
            var groups = new Dictionary<int, List<string>>();

            foreach (var line in lyricLines)
            {
                // 匹配时间戳
                var timeMatch = TimeStampRegex.Match(line);
                if (!timeMatch.Success)
                    continue;

                // 解析时间：分钟、秒、毫秒
                int minutes = int.Parse(timeMatch.Groups[1].Value);
                int seconds = int.Parse(timeMatch.Groups[2].Value);
                int milliseconds = int.Parse(timeMatch.Groups[3].Value);

                // 转换为毫秒
                int time = (minutes * 60 + seconds) * 1000 + milliseconds;

                // 移除时间戳，获取纯文本
                string cleanText = TimeStampRegex.Replace(line, "").Trim();

                // 添加到对应的分组
                if (!groups.ContainsKey(time))
                {
                    groups[time] = new List<string>();
                }

                groups[time].Add(cleanText);
            }

            return groups;
        }

        /// <summary>
        /// 解析歌词行组
        /// 处理原文和翻译，生成逐字时间轴
        /// </summary>
        /// <param name="startTime">开始时间</param>
        /// <param name="lines">歌词行列表（第一行通常是原文，第二行是翻译）</param>
        /// <returns>解析后的歌词行对象</returns>
        private static LyricsLine ParseLyricsLineGroup(int startTime, List<string> lines)
        {
            if (lines == null || lines.Count == 0)
                return null;

            var lyricsLine = new LyricsLine
            {
                StartTime = startTime,
                IsWordTiming = true
            };

            if (lines.Count > 0)
            {
                // 检查第一行是否为空（移除时间戳后）
                string cleanText = TimeStampRegex.Replace(lines[0], "").Trim();

                // 如果是空行，跳过该歌词行
                if (string.IsNullOrEmpty(cleanText))
                {
                    return null;
                }

                // 解析歌词文本，生成逐字时间轴
                ParseLyricsText(lines[0], lyricsLine);

                // 如果有第二行，作为翻译文本
                if (lines.Count > 1)
                {
                    lyricsLine.TranslationText = lines[1];
                }

                // 检查是否应该过滤这行歌词
                if (ShouldFilterLyricsText(lyricsLine.OriginalText, lyricsLine.TranslationText))
                {
                    return null;
                }
            }

            return lyricsLine;
        }

        /// <summary>
        /// 解析歌词文本，生成逐字时间轴
        /// 根据字符类型（中文、英文、标点）计算不同的显示时长
        /// </summary>
        /// <param name="lyricLine">原始歌词行（包含时间戳）</param>
        /// <param name="lyricsLine">要填充的歌词行对象</param>
        private static void ParseLyricsText(string lyricLine, LyricsLine lyricsLine)
        {
            try
            {
                var wordTimings = new List<WordTiming>();

                // 移除时间戳，获取纯文本
                string cleanText = TimeStampRegex.Replace(lyricLine, "").Trim();

                // 空行检查（防御性编程）
                if (string.IsNullOrEmpty(cleanText))
                {
                    return;
                }

                // Logger.Debug($"移除时间戳后的文本: '{cleanText}'");

                // 将文本拆分为字符或单词
                var textParts = SplitTextByCharacters(cleanText);

                // Logger.Debug($"拆分为 {textParts.Length} 个文本部分");

                if (textParts.Length == 0)
                    return;

                // 时间轴参数
                int baseCharDuration = 150;  // 基础字符持续时间（毫秒）
                int wordSpacing = 20;       // 字符间隔时间（毫秒）

                int currentTime = lyricsLine.StartTime;

                // 为每个字符/单词计算时间
                for (int i = 0; i < textParts.Length; i++)
                {
                    string textPart = textParts[i];

                    // 根据字符类型计算持续时间
                    int charDuration = baseCharDuration;
                    if (IsCJKCharacter(textPart[0]))
                    {
                        // CJK字符（中日韩）显示时间稍长
                        charDuration = 180;
                    }
                    else if (char.IsPunctuation(textPart[0]))
                    {
                        // 标点符号显示时间较短
                        charDuration = 80;
                    }
                    else if (textPart.Length > 1)
                    {
                        // 英文单词根据长度计算
                        charDuration = textPart.Length * 60;
                    }

                    // 创建词语时间对象
                    var wordTiming = new WordTiming
                    {
                        Text = textPart,
                        StartTime = currentTime,
                        EndTime = currentTime + charDuration
                    };

                    wordTimings.Add(wordTiming);
                    currentTime += charDuration + wordSpacing;
                }

                // 填充歌词行信息
                lyricsLine.OriginalText = cleanText;
                lyricsLine.WordTimings = wordTimings;
                lyricsLine.EndTime = currentTime;
            }
            catch (Exception ex)
            {
                Logger.Error($"解析歌词时出错: {ex.Message}");
                // 出错时至少保存纯文本
                lyricsLine.OriginalText = TimeStampRegex.Replace(lyricLine, "").Trim();
            }
        }

        /// <summary>
        /// 将文本按字符或单词拆分
        /// CJK字符和标点符号单独拆分，英文单词整体拆分
        /// </summary>
        /// <param name="text">要拆分的文本</param>
        /// <returns>拆分后的文本部分数组</returns>
        private static string[] SplitTextByCharacters(string text)
        {
            var parts = new List<string>();
            int i = 0;

            while (i < text.Length)
            {
                // 处理空格
                if (char.IsWhiteSpace(text[i]))
                {
                    parts.Add(" ");
                    i++;
                    continue;
                }

                // CJK字符或标点符号单独处理
                if (IsCJKCharacter(text[i]) || char.IsPunctuation(text[i]))
                {
                    parts.Add(text[i].ToString());
                    i++;
                }
                // 英文字母或数字组合成单词
                else if (char.IsLetterOrDigit(text[i]))
                {
                    int start = i;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '\''))
                    {
                        i++;
                    }
                    parts.Add(text.Substring(start, i - start));
                }
                // 其他字符单独处理
                else
                {
                    parts.Add(text[i].ToString());
                    i++;
                }
            }

            return parts.ToArray();
        }

        /// <summary>
        /// 判断字符是否为CJK（中日韩）字符
        /// </summary>
        /// <param name="c">要判断的字符</param>
        /// <returns>是否为CJK字符</returns>
        private static bool IsCJKCharacter(char c)
        {
            int code = (int)c;
            // CJK统一汉字
            return (code >= 0x4E00 && code <= 0x9FFF) ||
                   // 日文平假名
                   (code >= 0x3040 && code <= 0x309F) ||
                   // 日文片假名
                   (code >= 0x30A0 && code <= 0x30FF) ||
                   // 韩文音节
                   (code >= 0xAC00 && code <= 0xD7AF);
        }

        #endregion

        #region 歌词渲染

        /// <summary>
        /// 创建双行歌词视觉元素
        /// 支持原文和翻译同时显示
        /// </summary>
        /// <param name="lyricsLine">歌词行对象</param>
        /// <param name="config">配置对象</param>
        /// <param name="maxWidth">最大宽度</param>
        /// <param name="currentPosition">当前播放位置</param>
        /// <returns>包含歌词的Panel对象</returns>
        public static Panel CreateDualLineLyricsVisual(LyricsLine lyricsLine, LyricsConfig config, double maxWidth, int currentPosition = 0)
        {
            // 根据配置确定对齐方式
            HorizontalAlignment panelAlignment = HorizontalAlignment.Center;
            switch (config.Alignment.ToLower())
            {
                case "left":
                    panelAlignment = HorizontalAlignment.Left;
                    break;
                case "right":
                    panelAlignment = HorizontalAlignment.Right;
                    break;
                case "center":
                default:
                    panelAlignment = HorizontalAlignment.Center;
                    break;
            }

            // 创建主容器
            var mainPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = panelAlignment,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 如果有逐字时间轴信息，创建带动画的歌词
            if (lyricsLine.WordTimings != null && lyricsLine.WordTimings.Count > 0)
            {
                // 创建原文（带逐字动画）
                var originalPanel = CreateWordTimingLineVisual(lyricsLine.WordTimings, config, false, currentPosition, panelAlignment);
                mainPanel.Children.Add(originalPanel);

                // 如果有翻译且配置启用翻译，创建翻译文本
                if (lyricsLine.HasTranslation && config.ShowTranslation && !string.IsNullOrEmpty(lyricsLine.TranslationText))
                {
                    // 添加行间距
                    if (!string.IsNullOrWhiteSpace(lyricsLine.OriginalText))
                    {
                        mainPanel.Children.Add(new Border { Height = config.LineSpacing });
                    }

                    // 创建翻译文本（无动画）
                    var translationPanel = CreateRegularLineVisual(lyricsLine.TranslationText, config, true, panelAlignment);
                    mainPanel.Children.Add(translationPanel);
                }
            }
            else
            {
                // 没有时间轴信息，创建普通文本
                if (!string.IsNullOrEmpty(lyricsLine.OriginalText))
                {
                    var originalPanel = CreateRegularLineVisual(lyricsLine.OriginalText, config, false, panelAlignment);
                    mainPanel.Children.Add(originalPanel);
                }

                // 添加翻译文本（如果有）
                if (lyricsLine.HasTranslation && config.ShowTranslation && !string.IsNullOrEmpty(lyricsLine.TranslationText))
                {
                    // 添加行间距
                    if (!string.IsNullOrWhiteSpace(lyricsLine.OriginalText))
                    {
                        mainPanel.Children.Add(new Border { Height = config.LineSpacing });
                    }

                    var translationPanel = CreateRegularLineVisual(lyricsLine.TranslationText, config, true, panelAlignment);
                    mainPanel.Children.Add(translationPanel);
                }
            }

            return mainPanel;
        }

        /// <summary>
        /// 创建带逐字时间轴的歌词行
        /// 每个字或词都是独立的元素，支持单独的高亮动画
        /// </summary>
        private static Panel CreateWordTimingLineVisual(List<WordTiming> wordTimings, LyricsConfig config, bool isTranslation, int currentPosition, HorizontalAlignment alignment)
        {
            // 创建水平排列的容器
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = alignment,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 获取样式参数
            Brush defaultTextBrush = GetTextBrush(config, isTranslation);
            Brush highlightedTextBrush = GetHighlightBrush(config);
            int fontSize = GetFontSize(config, isTranslation);
            string fontFamily = GetFontFamily(config);

            // 更新进度缓存，实现平滑动画
            UpdateProgressCache(wordTimings, currentPosition);

            // 为每个词语创建视觉元素
            foreach (var wordTiming in wordTimings)
            {
                if (string.IsNullOrEmpty(wordTiming.Text))
                    continue;

                // 处理空格
                if (wordTiming.Text == " ")
                {
                    var spaceElement = CreateSpaceElement(fontFamily, fontSize);
                    panel.Children.Add(spaceElement);
                    continue;
                }

                FrameworkElement wordElement;
                if (highlightedTextBrush == null)
                {
                    // 如果禁用高亮，创建简单的文本元素
                    wordElement = CreateSimpleWordElement(wordTiming.Text, fontFamily, fontSize, defaultTextBrush);
                }
                else
                {
                    // 创建带高亮效果的元素
                    double progress = _wordProgressCache.ContainsKey(wordTiming) ? _wordProgressCache[wordTiming] : 0;
                    wordElement = CreateSmoothWordElement(
                        wordTiming.Text,
                        fontFamily,
                        fontSize,
                        defaultTextBrush,
                        highlightedTextBrush,
                        progress
                    );
                }

                panel.Children.Add(wordElement);
            }

            return panel;
        }

        /// <summary>
        /// 创建空格元素
        /// 确保空格具有正确的宽度
        /// </summary>
        private static FrameworkElement CreateSpaceElement(string fontFamily, int fontSize)
        {
            // 创建测量用的文本块
            var measuringBlock = new TextBlock
            {
                Text = " ",
                FontFamily = new FontFamily(fontFamily),
                FontSize = fontSize
            };

            // 测量空格的宽度
            measuringBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            measuringBlock.Arrange(new Rect(0, 0, measuringBlock.DesiredSize.Width, measuringBlock.DesiredSize.Height));

            double spaceWidth = measuringBlock.DesiredSize.Width;

            // 创建具有正确宽度的空元素
            return new Border
            {
                Width = spaceWidth,
                Height = measuringBlock.DesiredSize.Height,
                Background = Brushes.Transparent,
                Margin = new Thickness(0)
            };
        }

        /// <summary>
        /// 更新进度缓存
        /// 实现平滑的过渡动画，避免突兀的变化
        /// </summary>
        private static void UpdateProgressCache(List<WordTiming> wordTimings, int currentPosition)
        {
            foreach (var wordTiming in wordTimings)
            {
                if (wordTiming.Text == " ")
                    continue;

                // 计算目标进度
                double targetProgress = CalculateWordProgress(wordTiming, currentPosition);

                if (!_wordProgressCache.ContainsKey(wordTiming))
                {
                    // 首次设置
                    _wordProgressCache[wordTiming] = targetProgress;
                }
                else
                {
                    // 平滑过渡
                    double currentProgress = _wordProgressCache[wordTiming];
                    double diff = targetProgress - currentProgress;

                    // 如果差异很小，直接设置目标值
                    if (Math.Abs(diff) > 0.01)
                    {
                        // 使用30%的插值速度
                        _wordProgressCache[wordTiming] = currentProgress + diff * 0.3;
                    }
                    else
                    {
                        _wordProgressCache[wordTiming] = targetProgress;
                    }
                }
            }

            _lastPosition = currentPosition;
        }

        /// <summary>
        /// 更新动画效果
        /// 定时器触发的方法，清理过期的缓存
        /// </summary>
        private static void UpdateAnimations()
        {
            // 移除已经过期的缓存项（1秒前的）
            var expiredKeys = _wordProgressCache.Keys
                .Where(k => _lastPosition > k.EndTime + 1000)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _wordProgressCache.Remove(key);
            }
        }

        /// <summary>
        /// 计算词语的高亮进度
        /// </summary>
        private static double CalculateWordProgress(WordTiming wordTiming, int currentPosition)
        {
            if (currentPosition < wordTiming.StartTime)
                return 0.0;

            if (currentPosition >= wordTiming.EndTime)
                return 1.0;

            // 计算进度比例
            double totalDuration = wordTiming.EndTime - wordTiming.StartTime;
            if (totalDuration <= 0)
                return 1.0;

            double elapsed = currentPosition - wordTiming.StartTime;
            double progress = Math.Max(0.0, Math.Min(1.0, elapsed / totalDuration));

            // 应用缓动函数，使动画更自然
            progress = ApplyEasing(progress);

            return progress;
        }

        /// <summary>
        /// 应用缓动函数
        /// 使用立方缓动，使动画更平滑
        /// </summary>
        private static double ApplyEasing(double progress)
        {
            return 1 - Math.Pow(1 - progress, 3);
        }

        /// <summary>
        /// 创建带平滑高亮效果的词语元素
        /// 使用双层文本和裁剪实现平滑过渡
        /// </summary>
        private static FrameworkElement CreateSmoothWordElement(
            string text,
            string fontFamily,
            int fontSize,
            Brush defaultColor,
            Brush highlightColor,
            double progress)
        {
            // 使用Grid作为容器
            var grid = new Grid();

            // 底层文本（默认颜色）
            var baseTextBlock = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily(fontFamily),
                FontSize = fontSize,
                Foreground = defaultColor,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                TextWrapping = TextWrapping.NoWrap
            };

            // 顶层文本（高亮颜色）
            var highlightTextBlock = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily(fontFamily),
                FontSize = fontSize,
                Foreground = highlightColor,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                TextWrapping = TextWrapping.NoWrap
            };

            // 测量文本尺寸
            var measuringBlock = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily(fontFamily),
                FontSize = fontSize
            };

            measuringBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            measuringBlock.Arrange(new Rect(0, 0, measuringBlock.DesiredSize.Width, measuringBlock.DesiredSize.Height));

            double textWidth = measuringBlock.DesiredSize.Width;
            double highlightWidth = textWidth * progress;

            // 对顶层文本应用裁剪，实现渐进显示效果
            highlightTextBlock.Clip = new RectangleGeometry
            {
                Rect = new Rect(0, 0, highlightWidth, measuringBlock.DesiredSize.Height * 1.2)
            };

            // 将两层文本添加到Grid
            grid.Children.Add(baseTextBlock);
            grid.Children.Add(highlightTextBlock);

            // 设置Grid尺寸
            grid.Width = textWidth;
            grid.Height = measuringBlock.DesiredSize.Height * 1.2;

            grid.Margin = new Thickness(0);

            return grid;
        }

        /// <summary>
        /// 创建简单的词语元素（无高亮效果）
        /// </summary>
        private static FrameworkElement CreateSimpleWordElement(string text, string fontFamily, int fontSize, Brush textColor)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily(fontFamily),
                FontSize = fontSize,
                Foreground = textColor,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(0)
            };
            return textBlock;
        }

        /// <summary>
        /// 创建普通文本行视觉元素
        /// 用于没有时间轴的歌词或翻译文本
        /// </summary>
        private static Panel CreateRegularLineVisual(string text, LyricsConfig config, bool isTranslation, HorizontalAlignment alignment)
        {
            // 创建水平排列的容器
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = alignment,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 获取样式参数
            Brush textBrush = GetTextBrush(config, isTranslation);
            int fontSize = GetFontSize(config, isTranslation);
            string fontFamily = GetFontFamily(config);

            // 创建文本块
            var textBlock = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily(fontFamily),
                FontSize = fontSize,
                Foreground = textBrush,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap
            };

            panel.Children.Add(textBlock);
            return panel;
        }

        #endregion

        #region 样式辅助方法

        /// <summary>
        /// 获取高亮画刷
        /// 如果高亮颜色为"DISABLED"或无效，则返回null
        /// </summary>
        private static Brush GetHighlightBrush(LyricsConfig config)
        {
            // 检查是否禁用高亮
            if (string.IsNullOrEmpty(config.HighlightColor) || config.HighlightColor == "DISABLED")
            {
                return null; // 返回null表示不使用高亮
            }

            try
            {
                var brush = (Brush)new BrushConverter().ConvertFromString(config.HighlightColor);
                return brush;
            }
            catch (Exception ex)
            {
                Logger.Error($"解析高亮颜色时出错: {ex.Message}");
            }
            return Brushes.Cyan;
        }

        /// <summary>
        /// 获取文本画刷
        /// 根据是否为翻译文本选择合适的颜色
        /// </summary>
        private static Brush GetTextBrush(LyricsConfig config, bool isTranslation)
        {
            try
            {
                string color = isTranslation ?
                    (config.TranslationFontColor ?? config.FontColor) :
                    config.FontColor;

                if (!string.IsNullOrEmpty(color))
                {
                    var brush = (Brush)new BrushConverter().ConvertFromString(color);
                    return brush;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"解析字体颜色时出错: {ex.Message}");
            }

            return Brushes.White;
        }

        /// <summary>
        /// 获取字体大小
        /// 翻译文本通常使用较小的字体
        /// </summary>
        private static int GetFontSize(LyricsConfig config, bool isTranslation)
        {
            if (isTranslation)
            {
                return config.TranslationFontSize > 0 ? config.TranslationFontSize : Math.Max(config.FontSize - 2, 8);
            }
            return config.FontSize;
        }

        /// <summary>
        /// 获取字体族名称
        /// 处理常见的字体名称映射
        /// </summary>
        private static string GetFontFamily(LyricsConfig config)
        {
            string fontFamily = "MiSans";
            if (!string.IsNullOrEmpty(config.FontFamily) && config.FontFamily != "default")
            {
                fontFamily = config.FontFamily;

                // 字体名称映射
                if (fontFamily.Equals("MicrosoftYaHei", StringComparison.OrdinalIgnoreCase))
                    fontFamily = "Microsoft YaHei";
                else if (fontFamily.Equals("SimHei", StringComparison.OrdinalIgnoreCase))
                    fontFamily = "SimHei";
                else if (fontFamily.Equals("SimSun", StringComparison.OrdinalIgnoreCase))
                    fontFamily = "SimSun";
            }
            return fontFamily;
        }

        #endregion

        #region 歌词过滤

        /// <summary>
        /// 检查是否应该过滤歌词文本
        /// 使用正则表达式匹配制作人信息、许可声明等非歌词内容
        /// </summary>
        private static bool ShouldFilterLyricsText(string originalText, string translationText)
        {
            // 检查配置是否启用过滤
            var config = ConfigManager.CurrentConfig;
            if (!config.EnableLyricsFilter || string.IsNullOrEmpty(config.LyricsFilterRegex))
            {
                return false;
            }

            // 更新过滤正则表达式
            if (_filterRegex == null || _filterRegex.ToString() != config.LyricsFilterRegex)
            {
                try
                {
                    _filterRegex = new Regex(config.LyricsFilterRegex, RegexOptions.Compiled);
                }
                catch (Exception ex)
                {
                    Logger.Error($"歌词过滤正则表达式无效: {ex.Message}");
                    return false;
                }
            }

            // 检查原文是否匹配过滤规则
            if (!string.IsNullOrEmpty(originalText))
            {
                if (_filterRegex.IsMatch(originalText))
                {
                    Logger.Debug($"过滤歌词行: {originalText}");
                    return true;
                }
            }

            // 检查译文是否匹配过滤规则
            if (!string.IsNullOrEmpty(translationText) && config.ShowTranslation)
            {
                if (_filterRegex.IsMatch(translationText))
                {
                    Logger.Debug($"过滤歌词译文行: {translationText}");
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region 获取当前歌词行

        /// <summary>
        /// 根据当前播放位置获取应该显示的歌词行
        /// </summary>
        /// <param name="lyricsLines">所有歌词行</param>
        /// <param name="currentPosition">当前播放位置（毫秒）</param>
        /// <returns>当前应该显示的歌词行</returns>
        public static LyricsLine GetCurrentLyricsLine(List<LyricsLine> lyricsLines, int currentPosition)
        {
            if (lyricsLines == null || lyricsLines.Count == 0)
                return null;

            // 遍历所有歌词行
            for (int i = 0; i < lyricsLines.Count; i++)
            {
                var currentLine = lyricsLines[i];
                int nextLineStartTime = (i < lyricsLines.Count - 1) ? lyricsLines[i + 1].StartTime : int.MaxValue;

                // 检查是否在当前行的时间范围内
                if (currentPosition >= currentLine.StartTime && currentPosition < nextLineStartTime)
                {
                    return currentLine;
                }
            }

            // 如果位置早于第一行，返回第一行
            if (currentPosition < lyricsLines[0].StartTime)
            {
                return lyricsLines[0];
            }

            // 否则返回最后一行
            return lyricsLines[lyricsLines.Count - 1];
        }

        #endregion

        #region 缓存管理

        /// <summary>
        /// 清空缓存
        /// 在配置更改或重新加载歌词时调用
        /// </summary>
        public static void ClearCache()
        {
            _wordProgressCache.Clear();
            _filterRegex = null;
        }

        #endregion
    }
}