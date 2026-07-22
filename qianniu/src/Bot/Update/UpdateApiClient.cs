using Newtonsoft.Json;
using BotLib;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Bot.Update
{
    public class UpdateInfo
    {
        [JsonProperty("version")]
        public string Version { get; set; }
        [JsonProperty("download_url")]
        public string DownloadUrl { get; set; }
        [JsonProperty("description")]
        public string ReleaseNotes { get; set; }
        [JsonProperty("file_size")]
        public long FileSize { get; set; }
    }

    internal class UpdateApiResponse
    {
        [JsonProperty("data")]
        public UpdateInfo Data { get; set; }
        [JsonProperty("error")]
        public string Error { get; set; }
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
                Log.Info(string.Format("[Update][LatestRequest] Url={0}, Accept=application/json, ApiKeyAttached={1}", request.RequestUri, !string.IsNullOrEmpty(ApiKey)));
                using (var response = await HttpClient.SendAsync(request))
                {
                    var body = await response.Content.ReadAsStringAsync();
                    Log.Info(string.Format("[Update][LatestResponse] Status={0}, ContentType={1}, Body={2}", response.StatusCode, response.Content.Headers.ContentType == null ? "<empty>" : response.Content.Headers.ContentType.ToString(), body));
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(string.Format("检查更新接口请求失败，Status={0}，Body={1}", response.StatusCode, body));
                    }
                    var result = JsonConvert.DeserializeObject<UpdateApiResponse>(body);
                    if (result == null)
                    {
                        throw new InvalidOperationException("Update API returned an empty response.");
                    }
                    if (!string.IsNullOrEmpty(result.Error))
                    {
                        throw new InvalidOperationException("Update API error: " + result.Error);
                    }
                    if (result.Data == null)
                    {
                        throw new InvalidOperationException("Update API response does not contain data.");
                    }
                    Log.Info(string.Format("[Update][LatestParsed] Version={0}, DownloadUrl={1}, Description={2}, FileSize={3}", result.Data.Version ?? "<empty>", result.Data.DownloadUrl ?? "<empty>", result.Data.ReleaseNotes ?? "<empty>", result.Data.FileSize));
                    return result.Data;
                }
            }
        }

        public static async Task<byte[]> DownloadAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("Update download URL is empty.", "url");
            }
            Uri absoluteUri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out absoluteUri))
            {
                absoluteUri = new Uri(BaseUrl.TrimEnd('/') + "/" + url.TrimStart('/'));
            }
            using (var request = new HttpRequestMessage(HttpMethod.Get, absoluteUri))
            {
                request.Headers.TryAddWithoutValidation("accept", "application/json");
                request.Headers.TryAddWithoutValidation("x-api-key", ApiKey);
                Log.Info(string.Format("[Update][DownloadRequest] Url={0}, Accept=application/json, ApiKeyAttached={1}", request.RequestUri, !string.IsNullOrEmpty(ApiKey)));
                using (var response = await DownloadClient.SendAsync(request))
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    Log.Info(string.Format("[Update][DownloadResponse] Status={0}, ContentType={1}, HeaderLength={2}, DownloadedBytes={3}", response.StatusCode, response.Content.Headers.ContentType == null ? "<empty>" : response.Content.Headers.ContentType.ToString(), response.Content.Headers.ContentLength.HasValue ? response.Content.Headers.ContentLength.Value.ToString() : "<unknown>", bytes == null ? 0 : bytes.Length));
                    if (!response.IsSuccessStatusCode)
                    {
                        Log.Error("[Update][DownloadErrorBody] " + System.Text.Encoding.UTF8.GetString(bytes));
                        throw new InvalidOperationException(string.Format(
                            "Update download request failed. Status={0}, Body={1}",
                            response.StatusCode,
                            System.Text.Encoding.UTF8.GetString(bytes)));
                    }
                    return bytes;
                }
            }
        }
    }
}
