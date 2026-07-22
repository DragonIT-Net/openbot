using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Bot.Update
{
    public class UpdateInfo
    {
        [JsonProperty("version")]
        public string Version { get; set; }
        [JsonProperty("downloadUrl")]
        public string DownloadUrl { get; set; }
        [JsonProperty("releaseNotes")]
        public string ReleaseNotes { get; set; }
    }

    internal static class UpdateApiClient
    {
        // 跟智能工单接口同一台后端；如果后端要求单独发一个新 key，替换这里即可。
        private const string BaseUrl = "http://116.62.102.58";
        private const string ApiKey = "8b70ba53-d514-3140-d55f-9836a05a4171";
        private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private static readonly HttpClient DownloadClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        public static async Task<UpdateInfo> GetLatestAsync()
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl.TrimEnd('/') + "/update/latest"))
            {
                request.Headers.TryAddWithoutValidation("accept", "application/json");
                request.Headers.TryAddWithoutValidation("x-api-key", ApiKey);
                using (var response = await HttpClient.SendAsync(request))
                {
                    var body = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(string.Format("检查更新接口请求失败，Status={0}，Body={1}", response.StatusCode, body));
                    }
                    return JsonConvert.DeserializeObject<UpdateInfo>(body);
                }
            }
        }

        public static async Task<byte[]> DownloadAsync(string url)
        {
            return await DownloadClient.GetByteArrayAsync(url);
        }
    }
}
