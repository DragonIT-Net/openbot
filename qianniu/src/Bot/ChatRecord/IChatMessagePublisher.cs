using System.Collections.Generic;
using System.Threading.Tasks;

namespace Bot.ChatRecord
{
    public interface IChatMessagePublisher
    {
        Task PublishAsync(string sellerNick, string shopName, List<ChatMessageDto> messages);
    }
}
