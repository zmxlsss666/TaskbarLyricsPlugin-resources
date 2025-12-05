using System.Text.Json.Serialization;

namespace TaskbarLyrics.Models
{
    /// <summary>
    /// 歌词配置类
    /// 包含所有用户可自定义的歌词显示设置
    /// 使用JSON序列化，支持配置文件的保存和加载
    /// </summary>
    public class LyricsConfig
    {
        #region 字体相关设置

        /// <summary>
        /// 字体族名称
        /// 默认：MiSans
        /// </summary>
        [JsonPropertyName("font_family")]
        public string FontFamily { get; set; } = "MiSans";

        /// <summary>
        /// 字体大小（像素）
        /// 默认：16
        /// </summary>
        [JsonPropertyName("font_size")]
        public int FontSize { get; set; } = 16;

        /// <summary>
        /// 字体颜色（十六进制格式）
        /// 默认：#FFFFFF（白色）
        /// </summary>
        [JsonPropertyName("font_color")]
        public string FontColor { get; set; } = "#FFFFFF";

        #endregion

        #region 背景和对齐设置

        /// <summary>
        /// 背景颜色（十六进制格式）
        /// 默认：#00000000（透明）
        /// </summary>
        [JsonPropertyName("background_color")]
        public string BackgroundColor { get; set; } = "#00000000";

        /// <summary>
        /// 文本对齐方式
        /// 可选值：Left, Center, Right
        /// 默认：Center
        /// </summary>
        [JsonPropertyName("alignment")]
        public string Alignment { get; set; } = "Center";

        #endregion

        #region 翻译显示设置

        /// <summary>
        /// 是否显示翻译文本
        /// 默认：true
        /// </summary>
        [JsonPropertyName("show_translation")]
        public bool ShowTranslation { get; set; } = true;

        /// <summary>
        /// 翻译字体大小（像素）
        /// 默认：14
        /// </summary>
        [JsonPropertyName("translation_font_size")]
        public int TranslationFontSize { get; set; } = 14;

        /// <summary>
        /// 翻译字体颜色（十六进制格式）
        /// 默认：#CCCCCC（浅灰色）
        /// </summary>
        [JsonPropertyName("translation_font_color")]
        public string TranslationFontColor { get; set; } = "#CCCCCC";

        #endregion

        #region 布局设置

        /// <summary>
        /// 行间距（像素）
        /// 默认：2
        /// </summary>
        [JsonPropertyName("line_spacing")]
        public int LineSpacing { get; set; } = 2;

        /// <summary>
        /// 歌词显示宽度（像素）
        /// 0表示不限制
        /// 默认：800
        /// </summary>
        [JsonPropertyName("lyrics_width")]
        public int LyricsWidth { get; set; } = 800;

        #endregion

        #region 高亮效果设置

        /// <summary>
        /// 高亮颜色（十六进制格式）
        /// 默认：#FF00FFFF（青色）
        /// </summary>
        [JsonPropertyName("highlight_color")]
        public string HighlightColor { get; set; } = "#FF00FFFF";

        /// <summary>
        /// 是否使用渐变高亮效果
        /// 默认：false
        /// </summary>
        [JsonPropertyName("highlight_gradient")]
        public bool HighlightGradient { get; set; } = false;

        /// <summary>
        /// 是否启用高亮动画
        /// 默认：true
        /// </summary>
        [JsonPropertyName("highlight_animation")]
        public bool HighlightAnimation { get; set; } = true;

        #endregion

        #region 行为设置

        /// <summary>
        /// 全屏时是否隐藏歌词
        /// 默认：true
        /// </summary>
        [JsonPropertyName("hide_on_fullscreen")]
        public bool HideOnFullscreen { get; set; } = true;

        /// <summary>
        /// 日志级别
        /// 可选值：Auto, Off, Error, Info, Debug
        /// 默认：Auto
        /// </summary>
        [JsonPropertyName("log_level")]
        public string LogLevel { get; set; } = "Auto";

        #endregion

        #region 过滤设置

        /// <summary>
        /// 是否启用歌词过滤功能
        /// 过滤非歌词内容（如制作人信息等）
        /// 默认：true
        /// </summary>
        [JsonPropertyName("enable_lyrics_filter")]
        public bool EnableLyricsFilter { get; set; } = true;

        /// <summary>
        /// 歌词过滤正则表达式
        /// 用于匹配需要过滤的非歌词内容
        /// </summary>
        [JsonPropertyName("lyrics_filter_regex")]
        public string LyricsFilterRegex { get; set; } = "^([^：]*)：.*$|^([^:]*):.*$|^([^翻唱]*)翻唱.*$|^([^许可]*)许可.*$|^([^音乐人]*)音乐人.*$|^([^国风]*)国风.*$|^([^纯音乐]*)纯音乐.*$";

        #endregion

        #region 位置偏移设置

        /// <summary>
        /// X轴位置偏移量（像素）
        /// 正数向右，负数向左
        /// 默认：0
        /// </summary>
        [JsonPropertyName("position_offset_x")]
        public int PositionOffsetX { get; set; } = 0;

        /// <summary>
        /// Y轴位置偏移量（像素）
        /// 正数向下，负数向上
        /// 默认：0
        /// </summary>
        [JsonPropertyName("position_offset_y")]
        public int PositionOffsetY { get; set; } = 0;

        #endregion
    }
}