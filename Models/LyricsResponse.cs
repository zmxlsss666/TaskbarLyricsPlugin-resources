using Newtonsoft.Json;

namespace TaskbarLyrics.Models
{
    /// <summary>
    /// 歌词API响应模型
    /// 表示从歌词API服务器获取的歌词数据
    /// </summary>
    public class LyricsResponse
    {
        /// <summary>
        /// 响应状态
        /// "success" 表示成功，其他值表示失败
        /// </summary>
        [JsonProperty("status")]
        public string Status { get; set; }

        /// <summary>
        /// 歌词文本
        /// LRC格式的歌词内容，可能包含时间戳和翻译
        /// </summary>
        [JsonProperty("lyric")]
        public string Lyric { get; set; }

        /// <summary>
        /// 歌词来源
        /// 标识歌词的来源（本地、网络搜索等）
        /// </summary>
        [JsonProperty("source")]
        public string Source { get; set; }

        /// <summary>
        /// 是否为简化版本
        /// 指示歌词是否经过简化处理
        /// </summary>
        [JsonProperty("simplified")]
        public bool Simplified { get; set; }
    }
}