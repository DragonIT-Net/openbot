using Bot.ChromeNs;
using DbEntity.Response;
using DbEntity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Bot.Automation.ChatDeskNs;
using Bot.ChatRecord;
using Newtonsoft.Json;
using BotLib;
using System.Diagnostics;
using System.Threading;
using Bot.AssistWindow.Widget.Robot;
using BotLib.Wpf.Extensions;
using OpenAI.Chat;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using static Bot.Params;

namespace Bot.ChromeNs
{
    public class QN
    {
        public event EventHandler<BuyerSwitchedEventArgs> EvBuyerSwitched;
        public event EventHandler<SellerSwitchedEventArgs> EvSellerSwitched;
        public event EventHandler<MessageNotifyEventArgs> EvMessageNotity;
        public event EventHandler<RecieveNewMessageEventArgs> EvRecieveNewMessage;
        public event EventHandler<ShopRobotReceriveNewMessageEventArgs> EvShopRobotReceriveNewMessage;
        public static HashSet<QN> QNSet { get; set; }
        public string QnVersion { get; set; }

        private CDPClient cdp;
        public CDPClient CDP
        {
            get
            {
                return cdp;
            }
            set
            {
                cdp = value;
                cdp.EvBuyerSwitched -= Cdp_EvBuyerSwitched;
                cdp.EvMessageNotity -= Cdp_EvMessageNotity;
                cdp.EvRecieveNewMessage -= Cdp_EvRecieveNewMessage;
                cdp.EvSellerSwitched -= Cdp_EvSellerSwitched;
                cdp.EvShopRobotReceriveNewMessage -= Cdp_EvShopRobotReceriveNewMessage;

                cdp.EvBuyerSwitched += Cdp_EvBuyerSwitched;
                cdp.EvMessageNotity += Cdp_EvMessageNotity;
                cdp.EvRecieveNewMessage += Cdp_EvRecieveNewMessage;
                cdp.EvSellerSwitched += Cdp_EvSellerSwitched;
                cdp.EvShopRobotReceriveNewMessage += Cdp_EvShopRobotReceriveNewMessage;
            }
        }

        private LocalUser _seller;
        public LocalUser Seller
        {
            get
            {
                return _seller;
            }
            set
            {
                _seller = value;
            }
        }

        public QNRpa rpa;
        public QNRpa Rpa { get { return rpa; } }
        public Conversation Buyer { get; set; }

        public static QN CurQN = null;

        static QN()
        {
            QNSet = new HashSet<QN>();
        }

        public QN(LocalUser seller)
        {
            this._seller = seller;
            this.rpa = new QNRpa(this);
        }


        public async Task SendTextAsync(string buyer, string text)
        {
            try
            {
                var comparison = String.Compare(QnVersion, "9.19.06N", StringComparison.OrdinalIgnoreCase);
                if (comparison < 0)
                {
                    SendTimiMsg(buyer, text);
                }
                else if (rpa != null)
                {
                    await rpa.SendTextAsync(buyer, text);
                }
                else
                {
                    Log.Error("自动回复未初始化，取消本次发送。");
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        }


        public async void SendImageAsync(string buyer, string imagePath)
        {
            try
            {
                if (rpa == null)
                {
                    Log.Error("自动回复未初始化，取消本次图片发送。");
                    return;
                }
                await rpa.SendImageAsync(buyer, imagePath);
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        }

        private static string LocalUserToText(LocalUser user)
        {
            if (user == null)
            {
                return "<null>";
            }

            return string.Format("Nick={0}, Display={1}, TargetId={2}", user.Nick, user.Display, user.TargetId);
        }

        private static string ConversationToText(Conversation conversation)
        {
            if (conversation == null)
            {
                return "<null>";
            }

            return string.Format("Nick={0}, Display={1}, TargetId={2}, Ccode={3}",
                conversation.Nick,
                conversation.Display,
                conversation.TargetId,
                conversation.Ccode);
        }

        private static string UserIdToText(string role, dynamic user)
        {
            if (user == null)
            {
                return role + "=<null>";
            }

            return string.Format("{0}.Nick={1}, {0}.Display={2}, {0}.TargetId={3}, {0}.TargetType={4}",
                role,
                user.nick,
                user.display,
                user.targetId,
                user.targetType);
        }

        private static void DumpParsedEvent(string eventName, LocalUser seller, Conversation buyer)
        {
            try
            {
                Log.Info(string.Format("[千牛页面事件-解析] {0}: Seller[{1}], Buyer[{2}]",
                    eventName,
                    LocalUserToText(seller),
                    ConversationToText(buyer)));
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        }

        private static void DumpMessageNotify(string notifyContent)
        {
            try
            {
                Log.Info(string.Format("[千牛消息通知-解析] NotifyContent={0}", notifyContent));
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        }

        private static void DumpChatResponse(RecieveNewMessageEventArgs e, ChatResponse chatRes)
        {
            try
            {
                var count = chatRes == null || chatRes.result == null ? 0 : chatRes.result.Count;
                Log.Info(string.Format("[千牛收到消息-解析] Buyer={0}, Code={1}, Subcode={2}, Count={3}",
                    e == null ? string.Empty : e.Buyer,
                    chatRes == null ? 0 : chatRes.code,
                    chatRes == null ? 0 : chatRes.subcode,
                    count));

                if (chatRes == null || chatRes.result == null)
                {
                    return;
                }

                foreach (var m in chatRes.result)
                {
                    if (m == null)
                    {
                        continue;
                    }

                    var originalText = m.originalData == null ? string.Empty : m.originalData.text;
                    var headerTitle = m.originalData == null || m.originalData.header == null ? string.Empty : m.originalData.header.title;
                    var headerSummary = m.originalData == null || m.originalData.header == null ? string.Empty : m.originalData.header.summary;
                    var actionUrl = m.originalData == null ? string.Empty : m.originalData.actionUrl;
                    var fileId = m.originalData == null ? string.Empty : m.originalData.fileId;
                    var itemId = m.originalData == null ? string.Empty : m.originalData.itemId;
                    var ccode = m.cid == null ? string.Empty : m.cid.ccode;
                    var clientId = m.mcode == null ? string.Empty : m.mcode.clientId;
                    var messageId = m.mcode == null ? string.Empty : m.mcode.messageId;
                    var wwMsgId = m.ext == null ? 0 : m.ext.ww_msgid;
                    var isBuyerSend = m.loginid != null && m.toid != null && m.loginid.nick == m.toid.nick;
                    var hasImage = !string.IsNullOrEmpty(fileId)
                        && new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp" }.Any(ext => fileId.ToLower().EndsWith(ext));
                    var messageText = originalText + headerSummary;

                    Log.Info(string.Format(
                        "[千牛消息字段] TemplateId={0}, Status={1}, SelfState={2}, SendTime={3}, SortTimeMicrosecond={4}, Ccode={5}, ClientId={6}, MessageId={7}, WwMsgId={8}, IsBuyerSend={9}, HasImage={10}, ItemId={11}, Summary={12}, MessageText={13}, OriginalText={14}, HeaderTitle={15}, HeaderSummary={16}, ActionUrl={17}, FileId={18}, From[{19}], To[{20}], Login[{21}], BrowserId={22}",
                        m.templateId,
                        m.status,
                        m.selfState,
                        m.sendTime,
                        m.sortTimeMicrosecond,
                        ccode,
                        clientId,
                        messageId,
                        wwMsgId,
                        isBuyerSend,
                        hasImage,
                        itemId,
                        m.summary,
                        messageText,
                        originalText,
                        headerTitle,
                        headerSummary,
                        actionUrl,
                        fileId,
                        UserIdToText("From", m.fromid),
                        UserIdToText("To", m.toid),
                        UserIdToText("Login", m.loginid),
                        m.browserid));
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        }

        private void Cdp_EvShopRobotReceriveNewMessage(object sender, ShopRobotReceriveNewMessageEventArgs e)
        {
            DumpParsedEvent("onShopRobotReceriveNewMsgs", e.Seller, e.Buyer);
            if (Params.Robot.GetIsAutoReply())
            {
                //打开买家
                OpenChat(e.Buyer.Nick);
            }
            if (EvShopRobotReceriveNewMessage != null)
            {
                EvShopRobotReceriveNewMessage(this, e);
            }
        }

        private void Cdp_EvSellerSwitched(object sender, SellerSwitchedEventArgs e)
        {
            DumpParsedEvent("onChatDlgActive", e.Seller, e.Buyer);
            Seller = e.Seller;
            Buyer = e.Buyer;
            CurQN = this;
            var desk = Desk.Inst;
            if (desk != null)
            {
                desk.ChangeBuyer(e.Buyer.Nick);
                desk.ChangeSeller(e.Seller.Nick);
            }
            else
            {
                Log.Error("收到千牛店铺切换事件，但尚未检测到千牛接待窗口。");
            }

            if (EvSellerSwitched != null)
            {
                EvSellerSwitched(this, e);
            }
        }

        private async void Cdp_EvRecieveNewMessage(object sender, RecieveNewMessageEventArgs e)
        {
            try
            {
                if (EvRecieveNewMessage != null)
                {
                    EvRecieveNewMessage(this, e);
                }

                var chatRes = JsonConvert.DeserializeObject<ChatResponse>(e.Message);
                DumpChatResponse(e, chatRes);
                if (chatRes == null || chatRes.result == null)
                {
                    Log.Error("收到买家消息，但消息内容解析为空。");
                    return;
                }
                var messages = chatRes.result;
                foreach (var m in messages)
                {
                    if (m == null || m.fromid == null || m.toid == null || _seller == null)
                    {
                        continue;
                    }

                    if (m.fromid.nick != _seller.Nick && m.toid.nick == _seller.Nick)
                    {
                        var isAutoReply = Params.Robot.GetIsAutoReply();
                        var answer = MyOpenAI.GetAnswer(m.toid.nick, m.fromid.nick, m.summary);
                        var desk = Desk.Inst;
                        if (desk != null)
                        {
                            desk.AddConversation(m.toid.nick, m.fromid.nick, m.summary, answer, isAutoReply);
                        }
                        else
                        {
                            Log.Error("收到买家消息，但尚未检测到千牛接待窗口。");
                        }

                        if (isAutoReply && !string.IsNullOrEmpty(answer) && !answer.StartsWith("错误："))
                        {
                            await SendTextAsync(m.fromid.nick, answer);
                            await Task.Delay(2000);
                        }
                        else if (isAutoReply && !string.IsNullOrEmpty(answer) && answer.StartsWith("错误："))
                        {
                            Log.Error(answer);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }

        }


        private void Cdp_EvMessageNotity(object sender, MessageNotifyEventArgs e)
        {
            DumpMessageNotify(e.NotifyContent);
            if (EvMessageNotity != null)
            {
                EvMessageNotity(this, e);
            }
        }

        private void Cdp_EvBuyerSwitched(object sender, BuyerSwitchedEventArgs e)
        {
            DumpParsedEvent("onConversationChange", e.Seller, e.Buyer);
            Seller = e.Seller;
            Buyer = e.Buyer;
            CurQN = this;
            var desk = Desk.Inst;
            if (desk != null)
            {
                desk.ChangeBuyer(e.Buyer.Nick);
                desk.ChangeSeller(e.Seller.Nick);
            }
            else
            {
                Log.Error("收到千牛买家切换事件，但尚未检测到千牛接待窗口。");
            }
            if (EvBuyerSwitched != null)
            {
                EvBuyerSwitched(this, e);
            }
        }

        public static QN GetByNick(LocalUser seller)
        {
            var qn = QNSet.FirstOrDefault(q => q._seller.Nick == seller.Nick || q._seller.Display == seller.Display);
            if (qn == null)
            {
                qn = new QN(seller);
                QNSet.Add(qn);
            }
            return qn;
        }

        public void SendTimiMsg(string userId, string smartTip)
        {
            cdp.SendTimiMsg(userId, smartTip);
        }


        public void TransferContact(string contactID, string targetID, string reason = "")
        {
            cdp.TransferContact(contactID, targetID, reason);
        }

        public void LightOff(string ccode)
        {
            cdp.LightOff(ccode);
        }

        public void MarkRead(string ccode, string clientId, string messageId)
        {
            cdp.MarkRead(ccode, clientId, messageId);
        }

        public async Task<LocalUserResponse> GetCurrentUser()
        {
            return await cdp.GetCurrentUser();
        }

        public void InsertText2Inputbox(string uid, string text)
        {
            cdp.InsertText2Inputbox(uid, text);
        }

        public async Task<bool> IsInputboxEmpty()
        {
            return await cdp.IsInputboxEmpty();
        }

        public void BrowserUrl(string url)
        {
            cdp.BrowserUrl(url);
        }

        public void SendRemindPayCard(string encryptedBuyerId, string orderId)
        {
            cdp.SendRemindPayCard(encryptedBuyerId, orderId);
        }

        public void RecallMessage(string ccode, string clientId, string messageId)
        {
            cdp.RecallMessage(ccode, clientId, messageId);
        }

        public void OpenChat(string nick)
        {
            cdp.OpenChat(nick);
        }

        public void GetRemoteHisMsg(string ccode)
        {
            cdp.GetRemoteHisMsg(ccode);
        }


        public void SendCoupon(string buyerNick, string activityId)
        {
            cdp.SendCoupon(buyerNick, activityId);
        }

        public void CloseChat(string contactID)
        {
            cdp.CloseChat(contactID);
        }


        public async Task<AccountStatusResponse> GetAccountStatus()
        {
            return await cdp.GetAccountStatus();
        }

        public async Task<ItemRecordResponse> GetItemRecords(string encryptId)
        {
            return await cdp.GetItemRecords(encryptId);
        }


        public async Task<SearchUserResponse> SearchBuyerUser(string searchQuery)
        {
            return await cdp.SearchBuyerUser(searchQuery);
        }

        public async Task<BuyerInfoResponse> GetBuyerInfo(string encryptId)
        {
            return await cdp.GetBuyerInfo(encryptId);
        }

        public async Task<ZnkfTradeQueryResponse> GetBuyerTrades(string securityBuyerUid, string bizOrderId)
        {
            return await cdp.GetBuyerTrades(securityBuyerUid, bizOrderId);
        }

        public async Task<ConversationResponse> GetCurrentConversationID()
        {
            var res = await cdp.GetCurrentConversationID();
            if (res == null)
            {
                Log.Error("未获取到当前千牛会话。");
                return null;
            }
            if (CurQN == null && res.Result != null && (Buyer == null || Buyer.Nick != res.Result.Nick))
            {
                Buyer = res.Result;
                CurQN = this;
                var desk = Desk.Inst;
                if (desk != null)
                {
                    desk.ChangeBuyer(Buyer.Nick);
                }
                else
                {
                    Log.Error("当前会话已获取，但尚未检测到千牛接待窗口。");
                }
            }
            return res;
        }

    }
}
