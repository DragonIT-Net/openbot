using BotLib;
using Bot.Ticket;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace Bot.ChatRecord
{
    public class ChatMessagePublisher : IChatMessagePublisher
    {
        public async Task PublishAsync(string sellerNick, string shopName, List<ChatMessageDto> messages)
        {
            if (messages == null || messages.Count < 1) return;
            var body = JsonConvert.SerializeObject(new { sellerNick = sellerNick, shopName = shopName, messages = messages });
            await TicketApiClient.PostAsync("/ticket/chat/messages", body);
            Log.Info(string.Format("[聊天记录推送] Seller={0}, Shop={1}, Count={2}", sellerNick, shopName, messages.Count));
        }
    }
}
