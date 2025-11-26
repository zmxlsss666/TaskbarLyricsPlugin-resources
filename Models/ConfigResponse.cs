using System.Text.Json.Serialization;

namespace TaskbarLyrics.Models
{
    public class LyricsConfig
    {
        [JsonPropertyName("font_family")]
        public string FontFamily { get; set; } = "MiSans";
        
        [JsonPropertyName("font_size")]
        public int FontSize { get; set; } = 16;
        
        [JsonPropertyName("font_color")]
        public string FontColor { get; set; } = "#FFFFFF";
        
        [JsonPropertyName("background_color")]
        public string BackgroundColor { get; set; } = "#00000000";
        
        [JsonPropertyName("alignment")]
        public string Alignment { get; set; } = "Center";
        
        [JsonPropertyName("show_translation")]
        public bool ShowTranslation { get; set; } = true;
        
        [JsonPropertyName("translation_font_size")]
        public int TranslationFontSize { get; set; } = 14;
        
        [JsonPropertyName("translation_font_color")]
        public string TranslationFontColor { get; set; } = "#CCCCCC";
        
        [JsonPropertyName("line_spacing")]
        public int LineSpacing { get; set; } = 2;
        
        [JsonPropertyName("highlight_color")]
        public string HighlightColor { get; set; } = "#FF00FFFF";
        
        [JsonPropertyName("highlight_gradient")]
        public bool HighlightGradient { get; set; } = false;
        
        [JsonPropertyName("highlight_animation")]
        public bool HighlightAnimation { get; set; } = true;
    }
}