using System.Collections.Generic;

namespace Bot.Ticket
{
    public static class TicketFieldDefinitions
    {
        public static List<string> GetVisibleFields(TicketCategoryEnum category)
        {
            var fields = new List<string> { "CustomerIdentity", "Shop", "Description", "AttachmentPaths" };
            if (category == TicketCategoryEnum.InvoiceIssue)
            {
                fields.Add("Email");
                fields.Add("GuideSelfInvoice");
            }
            if (category == TicketCategoryEnum.TechIssue)
            {
                fields.Add("DeviceModel");
                fields.Add("ContactChannel");
                fields.Add("RemoteSoftware");
                fields.Add("CanRate");
            }
            return fields;
        }
    }
}
