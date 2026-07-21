using System.Threading.Tasks;

namespace Bot.Ticket
{
    public interface ITicketOptionProvider
    {
        Task<TicketOptionSet> GetOptionsAsync();
    }
}
