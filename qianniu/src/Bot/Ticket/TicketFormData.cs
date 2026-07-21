using System.Collections.Generic;
using Newtonsoft.Json;

namespace Bot.Ticket
{
    public class TicketFormData
    {
        public string Seller { get; set; }
        public string Buyer { get; set; }
        public TicketCategoryEnum Category { get; set; }
        public string CustomerIdentity { get; set; }
        public string Shop { get; set; }
        public string Description { get; set; }
        [JsonProperty("attachmentPath")]
        public List<string> AttachmentPaths { get; set; }

        // 兼容后续接口若采用复数命名 attachmentPaths。
        [JsonProperty("attachmentPaths")]
        private List<string> AttachmentPathsAlias
        {
            set
            {
                if (value != null) AttachmentPaths = value;
            }
        }
        public string Email { get; set; }
        public string GuideSelfInvoice { get; set; }
        public string DeviceModel { get; set; }
        public string ContactChannel { get; set; }
        public string RemoteSoftware { get; set; }
        public string CanRate { get; set; }
    }
}
