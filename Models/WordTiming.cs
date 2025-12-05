using System;
using System.Collections.Generic;

namespace TaskbarLyrics.Models
{
    /// <summary>
    /// 歌词行模型
    /// 表示一行歌词的完整信息，包括原文、翻译和逐字时间轴
    /// </summary>
    public class LyricsLine
    {
        /// <summary>
        /// 原始歌词文本
        /// </summary>
        public string OriginalText { get; set; } = string.Empty;

        /// <summary>
        /// 翻译文本
        /// 可以为空
        /// </summary>
        public string TranslationText { get; set; } = string.Empty;

        /// <summary>
        /// 是否有翻译文本
        /// 计算属性，用于快速判断
        /// </summary>
        public bool HasTranslation => !string.IsNullOrEmpty(TranslationText);

        /// <summary>
        /// 是否包含逐字时间轴信息
        /// true表示支持逐字同步，false表示整行同步
        /// </summary>
        public bool IsWordTiming { get; set; }

        /// <summary>
        /// 逐字时间轴列表
        /// 每个元素包含一个字或词的时间信息
        /// </summary>
        public List<WordTiming> WordTimings { get; set; } = new List<WordTiming>();

        /// <summary>
        /// 歌词行开始时间（毫秒）
        /// </summary>
        public int StartTime { get; set; }

        /// <summary>
        /// 歌词行结束时间（毫秒）
        /// </summary>
        public int EndTime { get; set; }
    }

    /// <summary>
    /// 词语时间模型
    /// 表示单个字或词的时间信息
    /// 用于实现逐字同步的高亮效果
    /// </summary>
    public class WordTiming
    {
        /// <summary>
        /// 文本内容
        /// 可以是一个汉字、一个英文单词或标点符号
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// 开始时间（毫秒）
        /// 该词应该开始高亮的时间
        /// </summary>
        public int StartTime { get; set; }

        /// <summary>
        /// 结束时间（毫秒）
        /// 该词应该结束高亮的时间
        /// </summary>
        public int EndTime { get; set; }
    }
}