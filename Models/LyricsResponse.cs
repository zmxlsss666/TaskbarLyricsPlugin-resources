using Newtonsoft.Json;

namespace TaskbarLyrics.Models
{
    public class LyricsResponse
    {
        [JsonProperty("status")]
        public string Status { get; set; }
        
        [JsonProperty("lyric")]
        public string Lyric { get; set; }
        
        [JsonProperty("source")]
        public string Source { get; set; }
        
        [JsonProperty("simplified")]
        public bool Simplified { get; set; }
    }
}