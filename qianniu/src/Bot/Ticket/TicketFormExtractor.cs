using BotLib;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

namespace Bot.Ticket
{
    public class TicketFormExtractor : ITicketFormExtractor
    {
        public async Task<TicketFormData> ExtractAsync(string sellerNick, string buyerNick, TicketCategoryEnum category)
        {
            try
            {
                var body = JsonConvert.SerializeObject(new { sellerNick = sellerNick, buyerNick = buyerNick, category = category.ToString() });
                var response = await TicketApiClient.PostAsync("/ticket/extract", body);
                Log.Info(string.Format("[智能工单提取返回] Seller={0}, Buyer={1}, Category={2}, Response={3}",
                    sellerNick,
                    buyerNick,
                    category,
                    response));
                var form = JsonConvert.DeserializeObject<TicketFormData>(response) ?? new TicketFormData();
                form.Seller = sellerNick;
                form.Buyer = buyerNick;
                form.Category = category;
                if (string.IsNullOrEmpty(form.CustomerIdentity)) form.CustomerIdentity = buyerNick;
                return form;
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                throw new InvalidOperationException("工单信息提取失败，请稍后重试。", ex);
            }
        }
    }
}
