using Newtonsoft.Json;

namespace TaskbarLyrics.Models
{
    /// <summary>
    /// 当前播放状态API响应模型
    /// 表示从API服务器获取的当前播放信息
    /// </summary>
    public class NowPlayingResponse
    {
        /// <summary>
        /// 响应状态
        /// "success" 表示成功，其他值表示失败
        /// </summary>
        [JsonProperty("status")]
        public string Status { get; set; }

        /// <summary>
        /// 歌曲标题
        /// </summary>
        [JsonProperty("title")]
        public string Title { get; set; }

        /// <summary>
        /// 艺术家/歌手名称
        /// </summary>
        [JsonProperty("artist")]
        public string Artist { get; set; }

        /// <summary>
        /// 专辑名称
        /// </summary>
        [JsonProperty("album")]
        public string Album { get; set; }

        /// <summary>
        /// 播放状态
        /// true表示正在播放，false表示已暂停
        /// </summary>
        [JsonProperty("isPlaying")]
        public bool IsPlaying { get; set; }

        /// <summary>
        /// 当前播放位置（毫秒）
        /// 用于歌词同步
        /// </summary>
        [JsonProperty("position")]
        public int Position { get; set; }

        /// <summary>
        /// 音量（0-100）
        /// </summary>
        [JsonProperty("volume")]
        public int Volume { get; set; }

        /// <summary>
        /// 时间戳
        /// Unix时间戳，表示响应生成时间
        /// </summary>
        [JsonProperty("timestamp")]
        public long Timestamp { get; set; }
    }
}