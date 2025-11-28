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
    public class LyricsRenderer
    {
        private const string TimeStampPattern = @"\[(\d+):(\d+)\.(\d+)\]";
        private static readonly Regex TimeStampRegex = new Regex(TimeStampPattern);

        private static DispatcherTimer _animationTimer;
        private static Dictionary<WordTiming, double> _wordProgressCache = new Dictionary<WordTiming, double>();
        private static int _lastPosition = -1;

        static LyricsRenderer()
        {
            _animationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _animationTimer.Tick += (s, e) => UpdateAnimations();
            _animationTimer.Start();
        }

        public static List<LyricsLine> ParseLyrics(string lyricsText)
        {
            var lines = new List<LyricsLine>();
            
            if (string.IsNullOrEmpty(lyricsText))
                return lines;

            var lyricLines = lyricsText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var timeStampGroups = GroupLinesByTimeStamp(lyricLines);
            
            foreach (var group in timeStampGroups)
            {
                var lyricsLine = ParseLyricsLineGroup(group.Key, group.Value);
                if (lyricsLine != null)
                {
                    lines.Add(lyricsLine);
                }
            }

            lines.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            
            for (int i = 0; i < lines.Count; i++)
            {
                if (i < lines.Count - 1)
                {
                    lines[i].EndTime = lines[i + 1].StartTime;
                }
                else
                {
                    lines[i].EndTime = lines[i].StartTime + 5000;
                }
            }
            
            return lines;
        }

        private static Dictionary<int, List<string>> GroupLinesByTimeStamp(string[] lyricLines)
        {
            var groups = new Dictionary<int, List<string>>();
            
            foreach (var line in lyricLines)
            {
                var timeMatch = TimeStampRegex.Match(line);
                if (!timeMatch.Success)
                    continue;

                int minutes = int.Parse(timeMatch.Groups[1].Value);
                int seconds = int.Parse(timeMatch.Groups[2].Value);
                int milliseconds = int.Parse(timeMatch.Groups[3].Value);
                int time = (minutes * 60 + seconds) * 1000 + milliseconds;

                string cleanText = TimeStampRegex.Replace(line, "").Trim();
                
                if (!groups.ContainsKey(time))
                {
                    groups[time] = new List<string>();
                }
                
                groups[time].Add(cleanText);
            }
            
            return groups;
        }

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
                ParseLyricsText(lines[0], lyricsLine);
                
                if (lines.Count > 1)
                {
                    lyricsLine.TranslationText = lines[1];
                }
            }

            return lyricsLine;
        }

        private static void ParseLyricsText(string lyricLine, LyricsLine lyricsLine)
        {
            try
            {
                var wordTimings = new List<WordTiming>();
                
                string cleanText = TimeStampRegex.Replace(lyricLine, "").Trim();
                
                if (string.IsNullOrEmpty(cleanText))
                {
                    Debug.WriteLine("No text content after removing timestamps");
                    return;
                }

                Debug.WriteLine($"Clean text: '{cleanText}'");

                var textParts = SplitTextByCharacters(cleanText);
                
                Debug.WriteLine($"Split into {textParts.Length} text parts");

                if (textParts.Length == 0)
                    return;

                int totalDuration = 4000;
                int baseCharDuration = 150;
                int wordSpacing = 20;
                
                int currentTime = lyricsLine.StartTime;
                
                for (int i = 0; i < textParts.Length; i++)
                {
                    string textPart = textParts[i];
                    
                    int charDuration = baseCharDuration;
                    if (IsCJKCharacter(textPart[0]))
                    {
                        charDuration = 180;
                    }
                    else if (char.IsPunctuation(textPart[0]))
                    {
                        charDuration = 80;
                    }
                    else if (textPart.Length > 1)
                    {
                        charDuration = textPart.Length * 60;
                    }

                    var wordTiming = new WordTiming
                    {
                        Text = textPart,
                        StartTime = currentTime,
                        EndTime = currentTime + charDuration
                    };
                    
                    wordTimings.Add(wordTiming);
                    currentTime += charDuration + wordSpacing;
                    
                    Debug.WriteLine($"Word {i}: '{wordTiming.Text}' Start={wordTiming.StartTime} End={wordTiming.EndTime}");
                }

                lyricsLine.OriginalText = cleanText;
                lyricsLine.WordTimings = wordTimings;
                lyricsLine.EndTime = currentTime;
                
                Debug.WriteLine($"Successfully parsed lyrics with {wordTimings.Count} words, total duration: {lyricsLine.EndTime - lyricsLine.StartTime}ms");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing lyrics: {ex.Message}");
                lyricsLine.OriginalText = TimeStampRegex.Replace(lyricLine, "").Trim();
            }
        }

        private static string[] SplitTextByCharacters(string text)
        {
            var parts = new List<string>();
            int i = 0;
            
            while (i < text.Length)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    parts.Add(" ");
                    i++;
                    continue;
                }

                if (IsCJKCharacter(text[i]) || char.IsPunctuation(text[i]))
                {
                    parts.Add(text[i].ToString());
                    i++;
                }
                else if (char.IsLetterOrDigit(text[i]))
                {
                    int start = i;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '\''))
                    {
                        i++;
                    }
                    parts.Add(text.Substring(start, i - start));
                }
                else
                {
                    parts.Add(text[i].ToString());
                    i++;
                }
            }

            return parts.ToArray();
        }

        private static bool IsCJKCharacter(char c)
        {
            int code = (int)c;
            return (code >= 0x4E00 && code <= 0x9FFF) ||
                   (code >= 0x3040 && code <= 0x309F) ||
                   (code >= 0x30A0 && code <= 0x30FF) ||
                   (code >= 0xAC00 && code <= 0xD7AF);
        }

        public static Panel CreateDualLineLyricsVisual(LyricsLine lyricsLine, LyricsConfig config, double maxWidth, int currentPosition = 0)
        {
            Debug.WriteLine($"Creating lyrics visual. WordTimings count: {lyricsLine.WordTimings?.Count}");
            Debug.WriteLine($"Current position: {currentPosition}, Highlight color: {config.HighlightColor}");
            
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

            var mainPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = panelAlignment,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (lyricsLine.WordTimings != null && lyricsLine.WordTimings.Count > 0)
            {
                Debug.WriteLine($"Rendering word timing lyrics with {lyricsLine.WordTimings.Count} words at position {currentPosition}");
                
                var originalPanel = CreateWordTimingLineVisual(lyricsLine.WordTimings, config, false, currentPosition, panelAlignment);
                mainPanel.Children.Add(originalPanel);

                if (lyricsLine.HasTranslation && config.ShowTranslation && !string.IsNullOrEmpty(lyricsLine.TranslationText))
                {
                    if (!string.IsNullOrWhiteSpace(lyricsLine.OriginalText))
                    {
                        mainPanel.Children.Add(new Border { Height = config.LineSpacing });
                    }
                    
                    var translationPanel = CreateRegularLineVisual(lyricsLine.TranslationText, config, true, panelAlignment);
                    mainPanel.Children.Add(translationPanel);
                }
            }
            else
            {
                Debug.WriteLine("No word timings available, falling back to regular lyrics");
                
                if (!string.IsNullOrEmpty(lyricsLine.OriginalText))
                {
                    var originalPanel = CreateRegularLineVisual(lyricsLine.OriginalText, config, false, panelAlignment);
                    mainPanel.Children.Add(originalPanel);
                }

                if (lyricsLine.HasTranslation && config.ShowTranslation && !string.IsNullOrEmpty(lyricsLine.TranslationText))
                {
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

        private static Panel CreateWordTimingLineVisual(List<WordTiming> wordTimings, LyricsConfig config, bool isTranslation, int currentPosition, HorizontalAlignment alignment)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = alignment,
                VerticalAlignment = VerticalAlignment.Center
            };

            Brush defaultTextBrush = GetTextBrush(config, isTranslation);
            Brush highlightedTextBrush = GetHighlightBrush(config);
            int fontSize = GetFontSize(config, isTranslation);
            string fontFamily = GetFontFamily(config);

            Debug.WriteLine($"Word timing line visual - Default: {defaultTextBrush}, Highlight: {highlightedTextBrush}, FontSize: {fontSize}, Alignment: {alignment}");

            UpdateProgressCache(wordTimings, currentPosition);

            foreach (var wordTiming in wordTimings)
            {
                if (string.IsNullOrEmpty(wordTiming.Text))
                    continue;

                if (wordTiming.Text == " ")
                {
                    var spaceElement = CreateSpaceElement(fontFamily, fontSize);
                    panel.Children.Add(spaceElement);
                    continue;
                }

                double progress = _wordProgressCache.ContainsKey(wordTiming) ? _wordProgressCache[wordTiming] : 0;

                var wordElement = CreateSmoothWordElement(
                    wordTiming.Text, 
                    fontFamily, 
                    fontSize, 
                    defaultTextBrush, 
                    highlightedTextBrush, 
                    progress
                );
                
                panel.Children.Add(wordElement);
            }

            return panel;
        }

        private static FrameworkElement CreateSpaceElement(string fontFamily, int fontSize)
        {
            var measuringBlock = new TextBlock
            {
                Text = " ",
                FontFamily = new FontFamily(fontFamily),
                FontSize = fontSize
            };
            
            measuringBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            measuringBlock.Arrange(new Rect(0, 0, measuringBlock.DesiredSize.Width, measuringBlock.DesiredSize.Height));
            
            double spaceWidth = measuringBlock.DesiredSize.Width;
            
            return new Border
            {
                Width = spaceWidth,
                Height = measuringBlock.DesiredSize.Height,
                Background = Brushes.Transparent,
                Margin = new Thickness(0)
            };
        }

        private static void UpdateProgressCache(List<WordTiming> wordTimings, int currentPosition)
        {
            foreach (var wordTiming in wordTimings)
            {
                if (wordTiming.Text == " ")
                    continue;

                double targetProgress = CalculateWordProgress(wordTiming, currentPosition);
                
                if (!_wordProgressCache.ContainsKey(wordTiming))
                {
                    _wordProgressCache[wordTiming] = targetProgress;
                }
                else
                {
                    double currentProgress = _wordProgressCache[wordTiming];
                    double diff = targetProgress - currentProgress;
                    
                    if (Math.Abs(diff) > 0.01)
                    {
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

        private static void UpdateAnimations()
        {
            var expiredKeys = _wordProgressCache.Keys
                .Where(k => _lastPosition > k.EndTime + 1000)
                .ToList();
                
            foreach (var key in expiredKeys)
            {
                _wordProgressCache.Remove(key);
            }
        }

        private static double CalculateWordProgress(WordTiming wordTiming, int currentPosition)
        {
            if (currentPosition < wordTiming.StartTime)
                return 0.0;
            
            if (currentPosition >= wordTiming.EndTime)
                return 1.0;
            
            double totalDuration = wordTiming.EndTime - wordTiming.StartTime;
            if (totalDuration <= 0)
                return 1.0;
                
            double elapsed = currentPosition - wordTiming.StartTime;
            double progress = Math.Max(0.0, Math.Min(1.0, elapsed / totalDuration));
            
            progress = ApplyEasing(progress);
            
            return progress;
        }

        private static double ApplyEasing(double progress)
        {
            return 1 - Math.Pow(1 - progress, 3);
        }

        private static FrameworkElement CreateSmoothWordElement(
            string text, 
            string fontFamily, 
            int fontSize, 
            Brush defaultColor, 
            Brush highlightColor, 
            double progress)
        {
            var grid = new Grid();
            
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
            
            highlightTextBlock.Clip = new RectangleGeometry
            {
                Rect = new Rect(0, 0, highlightWidth, measuringBlock.DesiredSize.Height * 1.2)
            };
            
            grid.Children.Add(baseTextBlock);
            grid.Children.Add(highlightTextBlock);
            
            grid.Width = textWidth;
            grid.Height = measuringBlock.DesiredSize.Height * 1.2;
            
            grid.Margin = new Thickness(0);
            
            return grid;
        }

        private static Panel CreateRegularLineVisual(string text, LyricsConfig config, bool isTranslation, HorizontalAlignment alignment)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = alignment,
                VerticalAlignment = VerticalAlignment.Center
            };

            Brush textBrush = GetTextBrush(config, isTranslation);
            int fontSize = GetFontSize(config, isTranslation);
            string fontFamily = GetFontFamily(config);

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

        private static Brush GetHighlightBrush(LyricsConfig config)
        {
            try
            {
                if (!string.IsNullOrEmpty(config.HighlightColor))
                {
                    var brush = (Brush)new BrushConverter().ConvertFromString(config.HighlightColor);
                    Debug.WriteLine($"Using highlight color: {config.HighlightColor}");
                    return brush;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing highlight color: {ex.Message}");
            }
            
            Debug.WriteLine("Using default highlight color: Cyan");
            return Brushes.Cyan;
        }

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
                Debug.WriteLine($"Error parsing color: {ex.Message}");
            }
            
            return Brushes.White;
        }

        private static int GetFontSize(LyricsConfig config, bool isTranslation)
        {
            if (isTranslation)
            {
                return config.TranslationFontSize > 0 ? config.TranslationFontSize : Math.Max(config.FontSize - 2, 8);
            }
            return config.FontSize;
        }

        private static string GetFontFamily(LyricsConfig config)
        {
            string fontFamily = "MiSans";
            if (!string.IsNullOrEmpty(config.FontFamily) && config.FontFamily != "default")
            {
                fontFamily = config.FontFamily;
                
                if (fontFamily.Equals("MicrosoftYaHei", StringComparison.OrdinalIgnoreCase))
                    fontFamily = "Microsoft YaHei";
                else if (fontFamily.Equals("SimHei", StringComparison.OrdinalIgnoreCase))
                    fontFamily = "SimHei";
                else if (fontFamily.Equals("SimSun", StringComparison.OrdinalIgnoreCase))
                    fontFamily = "SimSun";
            }
            return fontFamily;
        }

        public static LyricsLine GetCurrentLyricsLine(List<LyricsLine> lyricsLines, int currentPosition)
        {
            if (lyricsLines == null || lyricsLines.Count == 0)
                return null;

            for (int i = 0; i < lyricsLines.Count; i++)
            {
                var currentLine = lyricsLines[i];
                int nextLineStartTime = (i < lyricsLines.Count - 1) ? lyricsLines[i + 1].StartTime : int.MaxValue;
                
                if (currentPosition >= currentLine.StartTime && currentPosition < nextLineStartTime)
                {
                    return currentLine;
                }
            }

            if (currentPosition < lyricsLines[0].StartTime)
            {
                return lyricsLines[0];
            }

            return lyricsLines[lyricsLines.Count - 1];
        }

        public static void ClearCache()
        {
            _wordProgressCache.Clear();
        }
    }
}
