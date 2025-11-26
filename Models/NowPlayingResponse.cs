using Newtonsoft.Json;

namespace TaskbarLyrics.Models
{
    public class NowPlayingResponse
    {
        [JsonProperty("status")]
        public string Status { get; set; }
        
        [JsonProperty("title")]
        public string Title { get; set; }
        
        [JsonProperty("artist")]
        public string Artist { get; set; }
        
        [JsonProperty("album")]
        public string Album { get; set; }
        
        [JsonProperty("isPlaying")]
        public bool IsPlaying { get; set; }
        
        [JsonProperty("position")]
        public int Position { get; set; }
        
        [JsonProperty("volume")]
        public int Volume { get; set; }
        
        [JsonProperty("timestamp")]
        public long Timestamp { get; set; }
    }
}