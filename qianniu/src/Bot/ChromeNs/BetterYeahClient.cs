using BotLib;
using Bot.ChatRecord;
using DbEntity;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Bot.ChromeNs
{
    public class BetterYeahClient
    {
        private const string ApiUrl = "http://116.62.102.58/betteryeah/chat";
        private const string ApiKey = "8b70ba53-d514-3140-d55f-9836a05a4171";
        private const int MaxHistoryMessageCount = 20;
        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly ConcurrentDictionary<string, List<BetterYeahMessage>> buyerMessages =
            new ConcurrentDictionary<string, List<BetterYeahMessage>>();

        static BetterYeahClient()
        {
            httpClient.Timeout = TimeSpan.FromSeconds(60);
        }

        /// <summary>
        /// 只把买家/客服消息追加进对话历史，不发起接口调用。
        /// 由调用方（QN）决定什么时候真正调用 <see cref="RequestAnswerForHistoryAsync"/>。
        /// </summary>
        public static void AppendUserMessage(QN qn, QNChatMessage message)
        {
            if (qn == null || qn.Seller == null || message == null || message.fromid == null)
            {
                return;
            }

            var sellerNick = qn.Seller.Nick;
            var buyerNick = message.fromid.nick;
            var key = string.Format("{0}#{1}", sellerNick, buyerNick);
            var timestamp = ParseTimestamp(message.sendTime);

            var history = buyerMessages.GetOrAdd(key, id => new List<BetterYeahMessage>());
            lock (history)
            {
                history.Add(BetterYeahMessage.User(message.summary, timestamp));
                TrimHistory(history);
            }
        }

        /// <summary>
        /// 用当前历史快照（包含调用前已经 Append 进去的全部消息）发起一次请求。
        /// </summary>
        public static async Task<string> RequestAnswerForHistoryAsync(QN qn, string buyerNick, string buyerTargetId)
        {
            if (qn == null || qn.Seller == null || string.IsNullOrEmpty(buyerNick))
            {
                return "错误：BetterYeah调用失败，缺少千牛会话信息";
            }

            var sellerNick = qn.Seller.Nick;
            var assistantId = string.IsNullOrEmpty(qn.Seller.Display) ? qn.Seller.Nick : qn.Seller.Display;
            var key = string.Format("{0}#{1}", sellerNick, buyerNick);
            var resolvedTargetId = qn.Buyer == null || string.IsNullOrEmpty(qn.Buyer.TargetId)
                ? buyerTargetId
                : qn.Buyer.TargetId;
            var productIds = await GetProductIdsAsync(qn, resolvedTargetId);

            var history = buyerMessages.GetOrAdd(key, id => new List<BetterYeahMessage>());

            try
            {
                var request = new BetterYeahChatRequest
                {
                    user_id = buyerNick,
                    assistant_id = assistantId,
                    msg_list = GetHistorySnapshot(history),
                    product_id = productIds
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
                        Log.Error(string.Format("BetterYeah接口调用失败，Status={0}, Body={1}", response.StatusCode, responseText));
                        return "错误：BetterYeah接口调用失败";
                    }

                    var chatResponse = JsonConvert.DeserializeObject<BetterYeahChatResponse>(responseText);
                    if (chatResponse == null)
                    {
                        Log.Error("BetterYeah接口返回为空。");
                        return "错误：BetterYeah接口返回为空";
                    }
                    if (!string.IsNullOrEmpty(chatResponse.error))
                    {
                        Log.Error("BetterYeah接口返回错误：" + chatResponse.error);
                        return "错误：" + chatResponse.error;
                    }

                    var answer = chatResponse.data == null || chatResponse.data.messages == null
                        ? string.Empty
                        : string.Join(Environment.NewLine, chatResponse.data.messages.Where(msg => !string.IsNullOrEmpty(msg)));
                    if (string.IsNullOrEmpty(answer))
                    {
                        Log.Error("BetterYeah接口未返回回复内容。");
                        return "错误：BetterYeah接口未返回回复内容";
                    }

                    lock (history)
                    {
                        history.Add(BetterYeahMessage.Assistant(answer, ToUnixTimestamp(DateTime.Now)));
                        TrimHistory(history);
                    }

                    Log.Info(string.Format("BetterYeah回复成功，Seller={0}, Buyer={1}, ProductIds={2}",
                        sellerNick,
                        buyerNick,
                        productIds.Count < 1 ? "<empty>" : string.Join(",", productIds)));
                    return answer;
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                return "错误：BetterYeah接口调用异常";
            }
        }

        private static async Task<List<string>> GetProductIdsAsync(QN qn, string buyerTargetId)
        {
            var ids = new List<string>();
            try
            {
                if (qn.Buyer != null && !string.IsNullOrEmpty(qn.Buyer.TargetId))
                {
                    buyerTargetId = qn.Buyer.TargetId;
                }
                if (string.IsNullOrEmpty(buyerTargetId))
                {
                    return ids;
                }

                var itemRecord = await qn.GetItemRecords(buyerTargetId);
                AddItemIds(ids, itemRecord == null || itemRecord.data == null ? null : itemRecord.data.underInquiryItemList);
                AddItemIds(ids, itemRecord == null || itemRecord.data == null ? null : itemRecord.data.footPointItemList);
                AddItemIds(ids, itemRecord == null || itemRecord.data == null ? null : itemRecord.data.recentlyBoughtItemList);
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
            return ids.Distinct().ToList();
        }

        private static void AddItemIds(List<string> ids, List<ZnkfItem> items)
        {
            if (items == null)
            {
                return;
            }
            ids.AddRange(items
                .Where(item => item != null && item.itemId > 0)
                .Select(item => item.itemId.ToString()));
        }

        private static List<BetterYeahMessage> GetHistorySnapshot(List<BetterYeahMessage> history)
        {
            lock (history)
            {
                return history.Select(msg => msg.Clone()).ToList();
            }
        }

        private static void TrimHistory(List<BetterYeahMessage> history)
        {
            while (history.Count > MaxHistoryMessageCount)
            {
                history.RemoveAt(0);
            }
        }

        private static long ParseTimestamp(string sendTime)
        {
            long value;
            if (long.TryParse(sendTime, out value))
            {
                if (value > 9999999999)
                {
                    return value / 1000;
                }
                return value;
            }
            return ToUnixTimestamp(DateTime.Now);
        }

        private static long ToUnixTimestamp(DateTime time)
        {
            return (long)(time.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalSeconds;
        }
    }

    public class BetterYeahChatRequest
    {
        public string user_id { get; set; }
        public string assistant_id { get; set; }
        public List<BetterYeahMessage> msg_list { get; set; }
        public List<string> product_id { get; set; }
    }

    public class BetterYeahMessage
    {
        public string role { get; set; }
        public long timestamp { get; set; }
        public string type { get; set; }
        public BetterYeahContent content { get; set; }

        public static BetterYeahMessage User(string text, long timestamp)
        {
            return Create("user", text, timestamp);
        }

        public static BetterYeahMessage Assistant(string text, long timestamp)
        {
            return Create("assistant", text, timestamp);
        }

        public BetterYeahMessage Clone()
        {
            return Create(role, content == null ? string.Empty : content.text, timestamp);
        }

        private static BetterYeahMessage Create(string role, string text, long timestamp)
        {
            return new BetterYeahMessage
            {
                role = role,
                timestamp = timestamp,
                type = "TEXT",
                content = new BetterYeahContent
                {
                    text = text ?? string.Empty
                }
            };
        }
    }

    public class BetterYeahContent
    {
        public string text { get; set; }
    }

    public class BetterYeahChatResponse
    {
        public BetterYeahChatData data { get; set; }
        public string error { get; set; }
    }

    public class BetterYeahChatData
    {
        public string conversation_id { get; set; }
        public List<string> messages { get; set; }
        public bool transfer_to_human { get; set; }
    }
}
