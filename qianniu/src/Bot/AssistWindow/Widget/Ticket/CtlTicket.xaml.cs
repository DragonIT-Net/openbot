using Bot.ChromeNs;
using Bot.Common.Windows;
using Bot.Ticket;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Bot.AssistWindow.Widget.Ticket
{
    public partial class CtlTicket : UserControl
    {
        private readonly ITicketFormExtractor _extractor = new TicketFormExtractor();

        public CtlTicket()
        {
            InitializeComponent();
        }

        private async void btnConfirm_Click(object sender, RoutedEventArgs e)
        {
            var qn = QN.CurQN;
            if (qn == null || qn.Seller == null || qn.Buyer == null)
            {
                MessageBox.Show("请先在千牛中打开一个买家会话。", "智能工单", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var item = cmbCategory.SelectedItem as ComboBoxItem;
            TicketCategoryEnum category;
            if (item == null || !Enum.TryParse(item.Tag as string, out category))
            {
                return;
            }

            SetLoading(true);
            try
            {
                var form = await _extractor.ExtractAsync(qn.Seller.Nick, qn.Buyer.Nick, category);
                var window = new WndTicketForm(form, new TicketOptionProvider());
                window.Owner = Window.GetWindow(this);
                SetLoading(false);
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "智能工单", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetLoading(false);
            }
        }

        private void SetLoading(bool loading)
        {
            pnlLoading.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            cmbCategory.IsEnabled = !loading;
        }
    }
}
