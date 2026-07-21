using System.Threading.Tasks;

namespace Bot.Ticket
{
    public interface ITicketFormExtractor
    {
        Task<TicketFormData> ExtractAsync(string sellerNick, string buyerNick, TicketCategoryEnum category);
    }
}
