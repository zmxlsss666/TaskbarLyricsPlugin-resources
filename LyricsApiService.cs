using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TaskbarLyrics.Models;

namespace TaskbarLyrics
{
    /// <summary>
    /// 歌词API服务类
    /// 负责与本地歌词API服务器（端口35374）通信
    /// 提供歌词获取、配置管理、播放控制等功能
    /// </summary>
    public class LyricsApiService : IDisposable
    {
        #region 私有字段

        private readonly HttpClient _httpClient;

        // API端点常量 - 本地API服务器地址（端口35374）
        private const string LyricsApiUrl = "http://localhost:35374/api/lyric";           // 获取音乐内置歌词
        private const string LyricsPwApiUrl = "http://localhost:35374/api/lyricfile";    // 获取LCR歌词文件
        private const string NowPlayingApiUrl = "http://localhost:35374/api/now-playing"; // 获取当前播放信息
        private const string PlayPauseApiUrl = "http://localhost:35374/api/play-pause";    // 播放/暂停
        private const string NextTrackApiUrl = "http://localhost:35374/api/next-track";     // 下一首
        private const string PreviousTrackApiUrl = "http://localhost:35374/api/previous-track"; // 上一首

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数，初始化HTTP客户端
        /// </summary>
        public LyricsApiService()
        {
            _httpClient = new HttpClient();
            // 设置5秒超时，避免长时间等待
            _httpClient.Timeout = TimeSpan.FromSeconds(5);
        }

        #endregion

        #region 歌词相关API

        /// <summary>
        /// 获取歌词
        /// 优先尝试本地歌词API，失败后尝试联网搜索API
        /// </summary>
        /// <returns>歌词响应对象</returns>
        public async Task<LyricsResponse> GetLyricsAsync()
        {
            try
            {
                // 首先尝试获取本地歌词
                var response = await _httpClient.GetStringAsync(LyricsApiUrl);
                return JsonConvert.DeserializeObject<LyricsResponse>(response);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取本地歌词时出错: {ex.Message}");

                // 本地歌词失败，尝试联网搜索的歌词
                try
                {
                    var response = await _httpClient.GetStringAsync(LyricsPwApiUrl);
                    return JsonConvert.DeserializeObject<LyricsResponse>(response);
                }
                catch (Exception ex2)
                {
                    Debug.WriteLine($"从联网搜索API {LyricsPwApiUrl} 获取歌词时出错: {ex2.Message}");
                    return new LyricsResponse { Status = "error" };
                }
            }
        }

        #endregion

        #region 播放控制API

        /// <summary>
        /// 获取当前播放信息
        /// 包括歌曲标题、艺术家、播放位置和播放状态
        /// </summary>
        /// <returns>播放信息响应对象</returns>
        public async Task<NowPlayingResponse> GetNowPlayingAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync(NowPlayingApiUrl);
                return JsonConvert.DeserializeObject<NowPlayingResponse>(response);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取当前播放信息时出错: {ex.Message}");
                return new NowPlayingResponse { Status = "error" };
            }
        }

        /// <summary>
        /// 播放/暂停切换
        /// </summary>
        /// <returns>操作是否成功</returns>
        public async Task<bool> PlayPauseAsync()
        {
            try
            {
                // 注意：高频调用，不输出日志以避免日志泛滥
                var response = await _httpClient.GetAsync(PlayPauseApiUrl);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"播放/暂停时出错: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 播放下一首
        /// </summary>
        /// <returns>操作是否成功</returns>
        public async Task<bool> NextTrackAsync()
        {
            try
            {
                // 注意：高频调用，不输出日志以避免日志泛滥
                var response = await _httpClient.GetAsync(NextTrackApiUrl);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"下一曲时出错: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 播放上一首
        /// </summary>
        /// <returns>操作是否成功</returns>
        public async Task<bool> PreviousTrackAsync()
        {
            try
            {
                // 注意：高频调用，不输出日志以避免日志泛滥
                var response = await _httpClient.GetAsync(PreviousTrackApiUrl);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"上一曲时出错: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 资源释放

        /// <summary>
        /// 释放HTTP客户端资源
        /// </summary>
        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        #endregion
    }
}