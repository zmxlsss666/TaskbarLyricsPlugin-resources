using System;
using System.Collections.Generic;

namespace TaskbarLyrics.Models
{
    public class LyricsLine
    {
        public string OriginalText { get; set; } = string.Empty;
        public string TranslationText { get; set; } = string.Empty;
        public bool HasTranslation => !string.IsNullOrEmpty(TranslationText);
        public bool IsWordTiming { get; set; }
        public List<WordTiming> WordTimings { get; set; } = new List<WordTiming>();
        public int StartTime { get; set; }
        public int EndTime { get; set; }
    }

    public class WordTiming
    {
        public string Text { get; set; } = string.Empty;
        public int StartTime { get; set; }
        public int EndTime { get; set; }
    }
}