using System.Collections.Generic;
using Newtonsoft.Json;

namespace Bot.Ticket
{
    public class TicketOptionSet
    {
        [JsonProperty("shops")]
        public List<string> Shops { get; set; }
        [JsonProperty("DeviceModel")]
        public List<string> DeviceModels { get; set; }
        [JsonProperty("RemoteSoftware")]
        public List<string> RemoteSoftwares { get; set; }
        [JsonProperty("GuideSelfInvoice")]
        public List<string> GuideSelfInvoiceOptions { get; set; }
        [JsonProperty("CanRate")]
        public List<string> CanRateOptions { get; set; }
    }
}
