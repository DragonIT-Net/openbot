using System;
using Newtonsoft.Json;

namespace Bot.ChatRecord
{
    public class ChatMessageDto
    {
        [JsonProperty("ccode")]
        public string Ccode { get; set; }
        [JsonProperty("buyerNick")]
        public string BuyerNick { get; set; }
        [JsonProperty("fromNick")]
        public string FromNick { get; set; }
        [JsonProperty("toNick")]
        public string ToNick { get; set; }
        [JsonProperty("isBuyerSend")]
        public bool IsBuyerSend { get; set; }
        [JsonProperty("sendTime")]
        public DateTime SendTime { get; set; }
        [JsonProperty("templateId")]
        public int TemplateId { get; set; }
        [JsonProperty("text")]
        public string MessageText { get; set; }
        [JsonProperty("fileId")]
        public string FileId { get; set; }
        [JsonProperty("fileUrl")]
        public string FileUrl { get; set; }
        [JsonProperty("clientId")]
        public string ClientId { get; set; }
        [JsonProperty("messageId")]
        public string MessageId { get; set; }
        [JsonProperty("orderNo")]
        public string OrderNo { get; set; }
    }
}
