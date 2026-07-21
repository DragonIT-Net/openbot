using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Markup;
using Bot.Automation.ChatDeskNs;
using DbEntity;
using BotLib;
using BotLib.Db.Sqlite;
using BotLib.Wpf.Extensions;
using System.Security.Cryptography;
using Bot.ChromeNs;
using Bot.AssistWindow.Widget.Robot;
using System.Collections.Concurrent;
using System.Linq;
using OpenAI.Chat;
using BotLib.Extensions;
using SuperSocket.SocketEngine.Configuration;
using Top.Api.Domain;

namespace Bot.AssistWindow.Widget.Robot
{
    public partial class CtlRobot : UserControl
    {
        private Desk _desk;
        private RightPanel _rightPanel;
        private WndAssist _wndDontUse;
        private QN _preQN;
        private ConcurrentDictionary<string, List<CtlConversation>> buyerConversations;

        public CtlRobot(Desk desk, RightPanel rp)
        {
            InitializeComponent();
            _desk = desk;
            _rightPanel = rp;
            buyerConversations = new ConcurrentDictionary<string, List<CtlConversation>>();
            Loaded += CtlRobot_Loaded;
            ctlBuyerQueue.BuyerConfirmed += CtlBuyerQueue_BuyerConfirmed;
        }

        private WndAssist Wnd
        {
            get
            {
                if (_wndDontUse == null)
                {
                    _wndDontUse = (this.xFindParentWindow() as WndAssist);
                }
                return _wndDontUse;
            }
            set
            {
                _wndDontUse = value;
            }
        }

        private void CtlRobot_Loaded(object sender, RoutedEventArgs e)
        {
            cboxAuto.IsChecked = Params.Robot.GetIsAutoReply();
        }

        public void AddConversation(string seller, string buyer, string question, string answer,bool isAutoReply = false)
        {
            var key = string.Format("{0}#{1}", seller, buyer);
            var ctlConversation = CtlConversation.Create(question, answer, isAutoReply);
            var conversations = buyerConversations.xTryGetValue(key);
            if (conversations == null || conversations.Count < 1)
            {
                conversations = new List<CtlConversation>() { ctlConversation };
            }
            else
            {
                conversations.Add(ctlConversation);
            }

            buyerConversations.AddOrUpdate(key, id => conversations, (k, v) => conversations);

            if (QN.CurQN != null
                && QN.CurQN.Seller != null
                && QN.CurQN.Buyer != null
                && QN.CurQN.Seller.Nick == seller
                && QN.CurQN.Buyer.Nick == buyer)
            {
                grdTipNoConv.Visibility = Visibility.Collapsed;
                stkDialog.Children.Add(ctlConversation);
                scvBody.ScrollToEnd();
            }

            ctlBuyerQueue.UpdateAnswer(seller, buyer, question, answer, isAutoReply);
        }

        /// <summary>
        /// 消息合并机制（见 docs/adr/0002-buyer-message-burst-coalescing.md）开始处理某个买家的消息时调用，
        /// 在多买家汇总面板里显示"回复中"状态。
        /// </summary>
        public void MarkBuyerReplying(string seller, string buyer)
        {
            ctlBuyerQueue.MarkReplying(seller, buyer);
        }

        private async void CtlBuyerQueue_BuyerConfirmed(BuyerQueueItem item)
        {
            try
            {
                var qn = QN.FindBySellerNick(item.SellerNick);
                if (qn == null)
                {
                    Log.Error(string.Format("[买家队列] 未找到对应的千牛账号实例，Seller={0}", item.SellerNick));
                    return;
                }

                qn.OpenChat(item.BuyerNick);

                if (!item.IsAutoReply && !string.IsNullOrEmpty(item.Answer))
                {
                    await qn.PrepareTextAsync(item.BuyerNick, item.Answer);
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
            finally
            {
                ctlBuyerQueue.RemoveBuyer(item.BuyerNick);
            }
        }

        private void ShowGridTip(Grid gd)
        {
            grdTipNoConv.xIsVisible(gd == grdTipNoConv);
            grdShowConv.xIsVisible(gd == grdShowConv);
        }

        public void ReShowAfterQNChange()
        {
            if (QN.CurQN != null)
            {
                _preQN = QN.CurQN;

                RefreshItems();

                RefreshConversations();

            }
        }

        private void RefreshConversations()
        {
            var key = string.Format("{0}#{1}", _preQN.Seller.Nick, _preQN.Buyer.Nick);
            var conversations = buyerConversations.xTryGetValue(key);
            stkDialog.Children.Clear();
            if (conversations != null && conversations.Count > 0)
            {
                conversations.ForEach(conv => stkDialog.Children.Add(conv));
                scvBody.ScrollToEnd();
                grdTipNoConv.Visibility = Visibility.Collapsed;
            }
            else
            {
                ShowGridTip(grdTipNoConv);
                stkDialog.Children.Add(grdTipNoConv);
            }
        }

        private async void RefreshItems()
        {
            pgDownGoods.Visibility = Visibility.Collapsed;
            RemoveCtlGoods();
            var itemRecord = await _preQN.GetItemRecords(_preQN.Buyer.TargetId);
            if (itemRecord == null || itemRecord.data == null)
            {
                Log.Info(string.Format("[商品记录] Buyer={0}, 未返回商品记录。", _preQN.Buyer.Nick));
            }
            else
            {
                LogItemIds("咨询宝贝", itemRecord.data.underInquiryItemList);
                LogItemIds("足迹", itemRecord.data.footPointItemList);
                LogItemIds("最近购买", itemRecord.data.recentlyBoughtItemList);
            }
        }

        private void LogItemIds(string source, List<ZnkfItem> items)
        {
            var ids = items == null
                ? new List<long>()
                : items.Where(item => item != null && item.itemId > 0).Select(item => item.itemId).Distinct().ToList();
            Log.Info(string.Format("[商品记录] Buyer={0}, 来源={1}, 商品ID={2}",
                _preQN.Buyer.Nick,
                source,
                ids.Count < 1 ? "<empty>" : string.Join(",", ids)));
        }

        private void RemoveCtlGoods()
        {
            var idx = 0;
            while (idx < panelGoods.Children.Count)
            {
                if (panelGoods.Children[idx] is CtlOneGoods)
                {
                    panelGoods.Children.RemoveAt(idx);
                }
                else
                {
                    idx++;
                }
            }
        }

        public void ChangeBuyer(string buyer)
        {
            txtBuyer.Text = buyer;
            ReShowAfterQNChange();
        }

        private void cboxAuto_Click(object sender, RoutedEventArgs e)
        {
            Params.Robot.SetIsAutoReply(cboxAuto.IsChecked ?? false);
        }

        private void tab_Click(object sender, RoutedEventArgs e)
        {
            var selected = sender as ToggleButton;
            if (selected == null) return;

            btnChatTab.IsChecked = selected == btnChatTab;
            btnTicketTab.IsChecked = selected == btnTicketTab;
            btnQaTab.IsChecked = selected == btnQaTab;
            scvBody.Visibility = selected == btnChatTab ? Visibility.Visible : Visibility.Collapsed;
            ctlTicket.Visibility = selected == btnTicketTab ? Visibility.Visible : Visibility.Collapsed;
            pnlQa.Visibility = selected == btnQaTab ? Visibility.Visible : Visibility.Collapsed;
        }

        private void btnHidePanel_Click(object sender, RoutedEventArgs e)
        {
            if (Wnd != null)
            {
                Wnd.HidePanelRight();
            }
        }
    }
}
