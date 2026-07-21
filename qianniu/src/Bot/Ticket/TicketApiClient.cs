using BotLib;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Bot.Ticket
{
    internal static class TicketApiClient
    {
        // 当前 Python 智能工单服务。后续接入设置页时，仅替换这两个配置来源。
        private const string BaseUrl = "http://116.62.102.58";
        private const string ApiKey = "8b70ba53-d514-3140-d55f-9836a05a4171";
        private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        public static async Task<string> GetAsync(string path)
        {
            using (var request = CreateRequest(HttpMethod.Get, path, null))
            using (var response = await HttpClient.SendAsync(request))
            {
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(string.Format("智能工单接口请求失败，Status={0}，Body={1}", response.StatusCode, body));
                }
                return body;
            }
        }

        public static async Task<string> PostAsync(string path, string json)
        {
            using (var request = CreateRequest(HttpMethod.Post, path, json))
            using (var response = await HttpClient.SendAsync(request))
            {
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(string.Format("智能工单接口请求失败，Status={0}，Body={1}", response.StatusCode, body));
                }
                return body;
            }
        }

        public static async Task<byte[]> DownloadAsync(string url)
        {
            return await HttpClient.GetByteArrayAsync(url);
        }

        public static async Task PutFileAsync(string uploadUrl, byte[] data, string mediaType)
        {
            using (var content = new ByteArrayContent(data))
            {
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
                using (var response = await HttpClient.PutAsync(uploadUrl, content))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        throw new InvalidOperationException(string.Format("钉钉附件上传失败，Status={0}，Body={1}", response.StatusCode, body));
                    }
                }
            }
        }

        private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string json)
        {
            var request = new HttpRequestMessage(method, BaseUrl.TrimEnd('/') + path);
            request.Headers.TryAddWithoutValidation("accept", "application/json");
            request.Headers.TryAddWithoutValidation("x-api-key", ApiKey);
            if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return request;
        }
    }
}
