using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TaskbarLyrics.Models;

namespace TaskbarLyrics
{
    public class LyricsApiService
    {
        private readonly HttpClient _httpClient;
        private const string LyricsApiUrl = "http://localhost:35374/api/lyric";
        private const string LyricsPwApiUrl = "http://localhost:35374/api/lyricspw";
        private const string ConfigApiUrl = "http://localhost:35374/api/config";
        private const string NowPlayingApiUrl = "http://localhost:35374/api/now-playing";
        private const string PlayPauseApiUrl = "http://localhost:35374/api/play-pause";
        private const string NextTrackApiUrl = "http://localhost:35374/api/next-track";
        private const string PreviousTrackApiUrl = "http://localhost:35374/api/previous-track";

        public LyricsApiService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(5);
        }

        public async Task<LyricsResponse> GetLyricsAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync(LyricsApiUrl);
                return JsonConvert.DeserializeObject<LyricsResponse>(response);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting lyrics from {LyricsApiUrl}: {ex.Message}");
                try
                {
                    var response = await _httpClient.GetStringAsync(LyricsPwApiUrl);
                    return JsonConvert.DeserializeObject<LyricsResponse>(response);
                }
                catch (Exception ex2)
                {
                    Debug.WriteLine($"Error getting lyrics from backup API {LyricsPwApiUrl}: {ex2.Message}");
                    return new LyricsResponse { Status = "error" };
                }
            }
        }


        public async Task<NowPlayingResponse> GetNowPlayingAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync(NowPlayingApiUrl);
                return JsonConvert.DeserializeObject<NowPlayingResponse>(response);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting now playing: {ex.Message}");
                return new NowPlayingResponse { Status = "error" };
            }
        }

        public async Task<bool> PlayPauseAsync()
        {
            try
            {
                Debug.WriteLine($"Calling Play/Pause API (GET): {PlayPauseApiUrl}");
                var response = await _httpClient.GetAsync(PlayPauseApiUrl);
                Debug.WriteLine($"Play/Pause response status: {response.StatusCode}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in Play/Pause: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> NextTrackAsync()
        {
            try
            {
                Debug.WriteLine($"Calling Next Track API (GET): {NextTrackApiUrl}");
                var response = await _httpClient.GetAsync(NextTrackApiUrl);
                Debug.WriteLine($"Next Track response status: {response.StatusCode}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in Next Track: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> PreviousTrackAsync()
        {
            try
            {
                Debug.WriteLine($"Calling Previous Track API (GET): {PreviousTrackApiUrl}");
                var response = await _httpClient.GetAsync(PreviousTrackApiUrl);
                Debug.WriteLine($"Previous Track response status: {response.StatusCode}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in Previous Track: {ex.Message}");
                return false;
            }
        }
    }
}