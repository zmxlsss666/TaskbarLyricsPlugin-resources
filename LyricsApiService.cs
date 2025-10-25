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
        private const string LyricsApiUrl = "http://localhost:35374/api/lyricspw";
        private const string ConfigApiUrl = "http://localhost:35374/api/config";

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
            catch
            {
                return new LyricsResponse { Status = "error" };
            }
        }

        public async Task<ConfigResponse> GetConfigAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync(ConfigApiUrl);
                var configResponse = JsonConvert.DeserializeObject<ConfigResponse>(response);
                

                if (configResponse?.Config != null)
                {

                    var dynamicResponse = JsonConvert.DeserializeObject<dynamic>(response);
                    
                    if (dynamicResponse?.config != null)
                    {

                        if (dynamicResponse.config.font_size != null)
                        {
                            double fontSize = (double)dynamicResponse.config.font_size;
                            configResponse.Config.FontSize = (int)Math.Round(fontSize);
                        }
                        

                        if (dynamicResponse.config.translation_font_size != null)
                        {
                            double translationFontSize = (double)dynamicResponse.config.translation_font_size;
                            configResponse.Config.TranslationFontSize = (int)Math.Round(translationFontSize);
                        }
                    }
                }
                
                return configResponse;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error: {ex.Message}");
                return new ConfigResponse { Status = "error" };
            }
        }
    }
}