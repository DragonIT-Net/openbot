using BotLib;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Bot.ChromeNs
{
    public class AiReplyAnalysisClient
    {
        private const string ApiUrl = "http://116.62.102.58/ai-reply-analysis/submit";
        private const string ApiKey = "8b70ba53-d514-3140-d55f-9836a05a4171";
        private static readonly HttpClient httpClient = new HttpClient();

        static AiReplyAnalysisClient()
        {
            httpClient.Timeout = TimeSpan.FromSeconds(15);
        }

        public static async Task SubmitAsync(string shopName, string csName, string buyerNickname, string aiReply, string editedReply)
        {
            if (string.IsNullOrEmpty(buyerNickname)
                || string.IsNullOrEmpty(aiReply)
                || string.IsNullOrEmpty(editedReply))
            {
                return;
            }

            try
            {
                var request = new AiReplyAnalysisRequest
                {
                    shop_name = shopName ?? string.Empty,
                    cs_name = csName ?? string.Empty,
                    buyer_nickname = buyerNickname,
                    ai_reply = aiReply,
                    edited_reply = editedReply
                };
                var body = JsonConvert.SerializeObject(request);
                using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, ApiUrl))
                {
                    httpRequest.Headers.TryAddWithoutValidation("accept", "application/json");
                    httpRequest.Headers.TryAddWithoutValidation("x-api-key", ApiKey);
                    httpRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");

                    var response = await httpClient.SendAsync(httpRequest);
                    var responseText = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        Log.Error(string.Format("AI回复分析上报失败，Status={0}, Body={1}", response.StatusCode, responseText));
                        return;
                    }

                    var result = JsonConvert.DeserializeObject<AiReplyAnalysisResponse>(responseText);
                    if (result == null || !result.success)
                    {
                        Log.Error(string.Format("AI回复分析上报返回失败，Body={0}", responseText));
                        return;
                    }

                    Log.Info(string.Format("AI回复分析上报成功，Shop={0}, Cs={1}, Buyer={2}, Data={3}",
                        shopName,
                        csName,
                        buyerNickname,
                        result.data));
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        }
    }

    public class AiReplyAnalysisRequest
    {
        public string shop_name { get; set; }
        public string cs_name { get; set; }
        public string buyer_nickname { get; set; }
        public string ai_reply { get; set; }
        public string edited_reply { get; set; }
    }

    public class AiReplyAnalysisResponse
    {
        public int data { get; set; }
        public bool success { get; set; }
        public string error { get; set; }
    }
}
