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
using System.Collections.Concurrent;
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
        private static readonly ConcurrentDictionary<string, PendingAiReply> pendingAiReplies =
            new ConcurrentDictionary<string, PendingAiReply>();
        private static readonly ConcurrentDictionary<string, DateTime> processedIncomingMessages =
            new ConcurrentDictionary<string, DateTime>();
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

        private static string BuildAiReplyKey(string sellerNick, string buyerNick)
        {
            return string.Format("{0}#{1}", sellerNick ?? string.Empty, buyerNick ?? string.Empty);
        }

        private static void CachePendingAiReply(string sellerNick, string buyerNick, string aiReply)
        {
            if (string.IsNullOrEmpty(sellerNick)
                || string.IsNullOrEmpty(buyerNick)
                || string.IsNullOrEmpty(aiReply)
                || aiReply.StartsWith("错误："))
            {
                return;
            }

            pendingAiReplies[BuildAiReplyKey(sellerNick, buyerNick)] = new PendingAiReply
            {
                SellerNick = sellerNick,
                BuyerNick = buyerNick,
                AiReply = aiReply,
                CreatedAt = DateTime.Now
            };
            CleanupPendingAiReplies();
        }

        private static bool TryConsumePendingAiReply(string sellerNick, string buyerNick, out PendingAiReply pending)
        {
            pending = null;
            if (string.IsNullOrEmpty(sellerNick) || string.IsNullOrEmpty(buyerNick))
            {
                return false;
            }
            return pendingAiReplies.TryRemove(BuildAiReplyKey(sellerNick, buyerNick), out pending);
        }

        private static void CleanupPendingAiReplies()
        {
            var expiredAt = DateTime.Now.AddMinutes(-10);
            foreach (var item in pendingAiReplies.ToArray())
            {
                if (item.Value == null || item.Value.CreatedAt < expiredAt)
                {
                    PendingAiReply removed;
                    pendingAiReplies.TryRemove(item.Key, out removed);
                }
            }
        }

        private static string BuildIncomingMessageKey(string sellerNick, QNChatMessage message)
        {
            if (message == null)
            {
                return string.Empty;
            }

            var buyerNick = message.fromid == null ? string.Empty : message.fromid.nick;
            var ccode = message.cid == null ? string.Empty : message.cid.ccode;
            var clientId = message.mcode == null ? string.Empty : message.mcode.clientId;
            var messageId = message.mcode == null ? string.Empty : message.mcode.messageId;
            var text = message.originalData == null ? message.summary : (message.originalData.text ?? message.summary);
            return string.Format("{0}#{1}#{2}#{3}#{4}#{5}#{6}",
                sellerNick ?? string.Empty,
                buyerNick,
                ccode,
                messageId,
                clientId,
                message.sendTime ?? string.Empty,
                text ?? string.Empty);
        }

        private static bool TryMarkIncomingMessageProcessed(string sellerNick, QNChatMessage message)
        {
            var key = BuildIncomingMessageKey(sellerNick, message);
            if (string.IsNullOrEmpty(key))
            {
                return true;
            }

            CleanupProcessedIncomingMessages();
            return processedIncomingMessages.TryAdd(key, DateTime.Now);
        }

        private static void CleanupProcessedIncomingMessages()
        {
            var expiredAt = DateTime.Now.AddMinutes(-30);
            foreach (var item in processedIncomingMessages.ToArray())
            {
                if (item.Value < expiredAt)
                {
                    DateTime removed;
                    processedIncomingMessages.TryRemove(item.Key, out removed);
                }
            }
        }

        private static string GetShopName(string sellerName)
        {
            if (string.IsNullOrEmpty(sellerName))
            {
                return string.Empty;
            }

            var index = sellerName.IndexOf(':');
            if (index < 0)
            {
                index = sellerName.IndexOf('：');
            }
            return index < 0 ? sellerName : sellerName.Substring(0, index);
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

        public async Task PrepareTextAsync(string buyer, string text)
        {
            try
            {
                if (rpa != null)
                {
                    await rpa.PrepareTextAsync(buyer, text);
                }
                else
                {
                    InsertText2Inputbox(buyer, text);
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

                    if (!isBuyerSend)
                    {
                        Log.Info(string.Format(
                            "[发送回执候选] Buyer={0}, Seller={1}, Ccode={2}, ClientId={3}, MessageId={4}, SendTime={5}, Text={6}",
                            m.toid == null ? string.Empty : m.toid.nick,
                            m.fromid == null ? string.Empty : m.fromid.nick,
                            ccode,
                            clientId,
                            messageId,
                            m.sendTime,
                            messageText));
                        QueueAiReplyAnalysisIfNeeded(m, messageText);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        }

        private static void QueueAiReplyAnalysisIfNeeded(QNChatMessage message, string editedReply)
        {
            Task.Run(async () => await SubmitAiReplyAnalysisIfNeeded(message, editedReply));
        }

        private static async Task SubmitAiReplyAnalysisIfNeeded(QNChatMessage message, string editedReply)
        {
            try
            {
                if (message == null || message.fromid == null || message.toid == null)
                {
                    return;
                }
                if (string.IsNullOrEmpty(editedReply))
                {
                    return;
                }

                PendingAiReply pending;
                if (!TryConsumePendingAiReply(message.fromid.nick, message.toid.nick, out pending))
                {
                    Log.Info(string.Format("AI回复分析未上报：未匹配到待上报AI回复，Seller={0}, Buyer={1}",
                        message.fromid.nick,
                        message.toid.nick));
                    return;
                }

                var shopName = GetShopName(pending.SellerNick);
                var csName = message.loginid == null || string.IsNullOrEmpty(message.loginid.display)
                    ? pending.SellerNick
                    : message.loginid.display;
                await AiReplyAnalysisClient.SubmitAsync(shopName, csName, pending.BuyerNick, pending.AiReply, editedReply);
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
                Log.Info(string.Format("[QN事件入口] receiveNewMsg, Buyer={0}, MessageLength={1}",
                    e == null ? string.Empty : e.Buyer,
                    e == null || e.Message == null ? 0 : e.Message.Length));
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
                        if (!TryMarkIncomingMessageProcessed(_seller.Nick, m))
                        {
                            Log.Info(string.Format("[消息去重] 跳过重复买家消息，Seller={0}, Buyer={1}, ClientId={2}, MessageId={3}, SendTime={4}, Text={5}",
                                _seller.Nick,
                                m.fromid.nick,
                                m.mcode == null ? string.Empty : m.mcode.clientId,
                                m.mcode == null ? string.Empty : m.mcode.messageId,
                                m.sendTime,
                                m.summary));
                            continue;
                        }

                        var isAutoReply = Params.Robot.GetIsAutoReply();
                        var answer = await BetterYeahClient.GetAnswerAsync(this, m);
                        CachePendingAiReply(_seller.Nick, m.fromid.nick, answer);
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
                        else if (!isAutoReply && !string.IsNullOrEmpty(answer) && !answer.StartsWith("错误："))
                        {
                            await PrepareTextAsync(m.fromid.nick, answer);
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

    public class PendingAiReply
    {
        public string SellerNick { get; set; }
        public string BuyerNick { get; set; }
        public string AiReply { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
