using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TaskbarLyrics.Models;

namespace TaskbarLyrics
{
    public class LyricsRenderer
    {
        private const string TimeStampPattern = @"\[(\d+):(\d+)\.(\d+)\]";
        private static readonly Regex TimeStampRegex = new Regex(TimeStampPattern);

        public static LyricsLine ParseLyricsLine(string lyricLine)
        {
            var lyricsLine = new LyricsLine();
            
            if (string.IsNullOrEmpty(lyricLine))
                return lyricsLine;

            string[] lines = lyricLine.Split(new[] { " / ", " // ", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            
            if (lines.Length == 0)
                return lyricsLine;

            string originalLine = lines[0].Trim();
            lyricsLine.OriginalText = CleanLyricText(originalLine);
            
            if (lines.Length > 1)
            {
                string translationLine = lines[1].Trim();
                lyricsLine.TranslationText = CleanLyricText(translationLine);
            }

            return lyricsLine;
        }

        private static string CleanLyricText(string text)
        {
            string cleaned = TimeStampRegex.Replace(text, "").Trim();
            return Regex.Replace(cleaned, @"\s+", " ");
        }

        public static Panel CreateDualLineLyricsVisual(LyricsLine lyricsLine, LyricsConfig config, double maxWidth)
        {
            var mainPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = GetHorizontalAlignment(config.Alignment),
                VerticalAlignment = VerticalAlignment.Center
            };

            var originalPanel = CreateLineVisual(lyricsLine.OriginalText, config, false);
            mainPanel.Children.Add(originalPanel);

            if (lyricsLine.HasTranslation && config.ShowTranslation)
            {
                if (!string.IsNullOrWhiteSpace(lyricsLine.OriginalText))
                {
                    mainPanel.Children.Add(new Border { Height = config.LineSpacing });
                }
                
                var translationPanel = CreateLineVisual(lyricsLine.TranslationText, config, true);
                mainPanel.Children.Add(translationPanel);
            }

            return mainPanel;
        }

        private static Panel CreateLineVisual(string text, LyricsConfig config, bool isTranslation)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = GetHorizontalAlignment(config.Alignment),
                VerticalAlignment = VerticalAlignment.Center
            };

            Brush textBrush = Brushes.White;
            int fontSize = config.FontSize;

            try
            {
                if (isTranslation)
                {
                    if (!string.IsNullOrEmpty(config.TranslationFontColor))
                        textBrush = (Brush)new BrushConverter().ConvertFromString(config.TranslationFontColor);
                    else if (!string.IsNullOrEmpty(config.FontColor))
                        textBrush = (Brush)new BrushConverter().ConvertFromString(config.FontColor);
                    
                    fontSize = config.TranslationFontSize > 0 ? config.TranslationFontSize : Math.Max(config.FontSize - 2, 8);
                }
                else
                {
                    if (!string.IsNullOrEmpty(config.FontColor))
                        textBrush = (Brush)new BrushConverter().ConvertFromString(config.FontColor);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error: {ex.Message}");
                textBrush = Brushes.White;
            }

            string fontFamily = "Segoe UI";
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

            var textBlock = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily(fontFamily),
                FontSize = fontSize,
                Foreground = textBrush,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = GetHorizontalAlignment(config.Alignment),
                TextWrapping = TextWrapping.NoWrap
            };

            panel.Children.Add(textBlock);
            return panel;
        }

        private static HorizontalAlignment GetHorizontalAlignment(string alignment)
        {
            if (string.IsNullOrEmpty(alignment))
                return HorizontalAlignment.Center;
                
            return alignment.ToLower() switch
            {
                "left" => HorizontalAlignment.Left,
                "right" => HorizontalAlignment.Right,
                "center" => HorizontalAlignment.Center,
                _ => HorizontalAlignment.Center
            };
        }
    }
}