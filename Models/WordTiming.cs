using System;
using System.Collections.Generic;

namespace TaskbarLyrics.Models
{
    public class LyricsLine
    {
        public string OriginalText { get; set; } = string.Empty;
        public string TranslationText { get; set; } = string.Empty;
        public bool HasTranslation => !string.IsNullOrEmpty(TranslationText);
    }
}