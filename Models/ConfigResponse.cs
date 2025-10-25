using System;
using Newtonsoft.Json;

namespace TaskbarLyrics.Models
{
    public class ConfigResponse
    {
        [JsonProperty("status")]
        public string Status { get; set; } = "success";
        
        [JsonProperty("config")]
        public LyricsConfig Config { get; set; } = new LyricsConfig();
        
        [JsonProperty("timestamp")]
        public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public class LyricsConfig
    {
        [JsonProperty("font_family")]
        public string FontFamily { get; set; } = "Segoe UI";
        
        [JsonProperty("font_size")]
        public int FontSize { get; set; } = 16; 
        
        [JsonProperty("font_color")]
        public string FontColor { get; set; } = "#FFFFFF";
        
        [JsonProperty("background_color")]
        public string BackgroundColor { get; set; } = "#00000000";
        
        [JsonProperty("alignment")]
        public string Alignment { get; set; } = "Center";
        
        [JsonProperty("show_translation")]
        public bool ShowTranslation { get; set; } = true;
        
        [JsonProperty("translation_font_size")]
        public int TranslationFontSize { get; set; } = 14; 
        
        [JsonProperty("translation_font_color")]
        public string TranslationFontColor { get; set; } = "#FFFFFF";
        
        [JsonProperty("line_spacing")]
        public int LineSpacing { get; set; } = 2;
    }
}