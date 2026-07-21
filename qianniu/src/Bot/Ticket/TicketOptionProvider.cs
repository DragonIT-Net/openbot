using System.Collections.Generic;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace Bot.Ticket
{
    public class TicketOptionProvider : ITicketOptionProvider
    {
        public async Task<TicketOptionSet> GetOptionsAsync()
        {
            var response = await TicketApiClient.GetAsync("/ticket/options");
            var options = JsonConvert.DeserializeObject<TicketOptionSet>(response) ?? new TicketOptionSet();
            options.Shops = options.Shops ?? new List<string>();
            options.DeviceModels = options.DeviceModels ?? new List<string>();
            options.RemoteSoftwares = options.RemoteSoftwares ?? new List<string>();
            options.GuideSelfInvoiceOptions = options.GuideSelfInvoiceOptions ?? new List<string>();
            options.CanRateOptions = options.CanRateOptions ?? new List<string>();
            return options;
        }
    }
}
