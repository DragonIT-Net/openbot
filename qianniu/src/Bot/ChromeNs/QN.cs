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
using Bot.Ticket;
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
        private static readonly IChatMessagePublisher chatMessagePublisher = new ChatMessagePublisher();
        // 买家连续碎片消息合并：谁正在处理中、消息到达版本号、当前批次的原始消息（详见 docs/adr/0002-buyer-message-burst-coalescing.md）
        private static readonly ConcurrentDictionary<string, byte> burstProcessing =
            new ConcurrentDictionary<string, byte>();
        private static readonly ConcurrentDictionary<string, int> burstVersion =
            new ConcurrentDictionary<string, int>();
        private static readonly ConcurrentDictionary<string, List<QNChatMessage>> burstMessages =
            new ConcurrentDictionary<string, List<QNChatMessage>>();
        private const int MaxBurstRetries = 3;
        // 买家从订单详情页点进来咨询时，千牛会自动插入一条"当前用户来自 订单XXX"的系统提示卡片，
        // 结构上跟普通消息一样但 originalData 为空、templateId 固定是 129，不是买家真正打的字，不能算进发给 AI 的对话历史
        private const int EntryIntoStoreTemplateId = 129;
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

        private async Task PublishChatMessagesAsync(List<QNChatMessage> messages)
        {
            if (_seller == null || messages == null || messages.Count < 1)
            {
                return;
            }

            var payload = new List<ChatMessageDto>();
            foreach (var message in messages)
            {
                if (message == null) continue;
                DateTime sendTime;
                if (!DateTime.TryParse(message.sendTime, out sendTime)) sendTime = DateTime.Now;
                var fromNick = message.fromid == null ? string.Empty : message.fromid.nick;
                var toNick = message.toid == null ? string.Empty : message.toid.nick;
                var fileUrl = GetMessageFileUrl(message.originalData);
                var messageText = message.summary ?? string.Empty;
                if (!string.IsNullOrEmpty(fileUrl))
                {
                    messageText = string.Format("{0}\n图片链接：{1}", string.IsNullOrEmpty(messageText) ? "[图片]" : messageText, fileUrl);
                }
                payload.Add(new ChatMessageDto
                {
                    Ccode = message.cid == null ? string.Empty : message.cid.ccode,
                    BuyerNick = fromNick == _seller.Nick ? toNick : fromNick,
                    FromNick = fromNick,
                    ToNick = toNick,
                    IsBuyerSend = fromNick != _seller.Nick,
                    SendTime = sendTime,
                    TemplateId = message.templateId,
                    MessageText = messageText,
                    FileId = message.originalData == null ? string.Empty : message.originalData.fileId,
                    FileUrl = fileUrl,
                    ClientId = message.mcode == null ? string.Empty : message.mcode.clientId,
                    MessageId = message.mcode == null ? string.Empty : message.mcode.messageId,
                    OrderNo = string.Empty
                });
            }

            await chatMessagePublisher.PublishAsync(_seller.Nick, GetShopName(_seller.Nick), payload);
        }

        private static string GetMessageFileUrl(OriginalData originalData)
        {
            if (originalData == null) return string.Empty;
            if (!string.IsNullOrEmpty(originalData.Url)) return originalData.Url;
            if (originalData.jsview == null) return string.Empty;
            var imageView = originalData.jsview.FirstOrDefault(item => item != null && item.value != null && !string.IsNullOrEmpty(item.value.url));
            return imageView == null ? string.Empty : imageView.value.url;
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
                    var fileUrl = GetMessageFileUrl(m.originalData);
                    var hasImage = !string.IsNullOrEmpty(fileId)
                        || (!string.IsNullOrEmpty(fileUrl)
                            && new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp" }
                                .Any(ext => fileUrl.ToLower().Contains(ext)));
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
                        string.IsNullOrEmpty(fileUrl) ? actionUrl : fileUrl,
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

        /// <summary>
        /// 买家连续快速发送多条碎片消息时，只触发一次 AI 回复：消息到达立即追加历史，
        /// 没有正在处理的请求时才真正调用接口；请求期间历史又变长的话丢弃结果、用最新历史重试（最多 <see cref="MaxBurstRetries"/> 次）。
        /// 详见 docs/adr/0002-buyer-message-burst-coalescing.md。
        /// </summary>
        private async Task HandleBuyerMessageWithCoalescingAsync(QNChatMessage m)
        {
            var buyerKey = BuildAiReplyKey(_seller.Nick, m.fromid.nick);

            BetterYeahClient.AppendUserMessage(this, m);
            burstMessages.AddOrUpdate(buyerKey,
                key => new List<QNChatMessage> { m },
                (key, list) =>
                {
                    lock (list)
                    {
                        list.Add(m);
                    }
                    return list;
                });
            burstVersion.AddOrUpdate(buyerKey, 1, (key, v) => v + 1);

            if (!burstProcessing.TryAdd(buyerKey, 0))
            {
                // 已经有一个请求在处理这个买家的消息，这条消息追加历史后交给那次请求处理即可，不重复发起
                return;
            }

            var desk = Desk.Inst;
            if (desk != null)
            {
                desk.MarkBuyerReplying(_seller.Nick, m.fromid.nick);
            }

            try
            {
                string answer = null;
                for (int attempt = 1; attempt <= MaxBurstRetries; attempt++)
                {
                    int versionBefore;
                    burstVersion.TryGetValue(buyerKey, out versionBefore);

                    answer = await BetterYeahClient.RequestAnswerForHistoryAsync(this, m.fromid.nick, m.fromid.targetId);

                    int versionAfter;
                    burstVersion.TryGetValue(buyerKey, out versionAfter);
                    if (versionAfter == versionBefore || attempt == MaxBurstRetries)
                    {
                        break;
                    }
                    Log.Info(string.Format("[消息合并] 请求期间买家又发来新消息，重新请求。Seller={0}, Buyer={1}, 第{2}次重试",
                        _seller.Nick, m.fromid.nick, attempt));
                }

                List<QNChatMessage> burst;
                burstMessages.TryRemove(buyerKey, out burst);
                burstVersion.TryRemove(buyerKey, out _);

                var combinedQuestion = burst == null || burst.Count < 1
                    ? m.summary
                    : string.Join(string.Empty, burst.Select(bm => bm == null ? string.Empty : bm.summary));

                var isAutoReply = Params.Robot.GetIsAutoReply();
                CachePendingAiReply(_seller.Nick, m.fromid.nick, answer);
                if (desk != null)
                {
                    desk.AddConversation(m.toid.nick, m.fromid.nick, combinedQuestion, answer, isAutoReply);
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
            finally
            {
                burstProcessing.TryRemove(buyerKey, out _);
            }
        }

        private async void Cdp_EvShopRobotReceriveNewMessage(object sender, ShopRobotReceriveNewMessageEventArgs e)
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

            // 非聚焦买家的新消息通知：inject.js 已经把最新消息内容一并拉回来了，
            // 走跟 receiveNewMsg 完全相同的处理流水线（去重、进店提示过滤、消息合并），
            // 这样不用切换聚焦也能处理，切换聚焦后重复收到同一条消息也会被去重挡掉。
            await ProcessIncomingMessagesAsync(e.Messages);
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
                await ProcessIncomingMessagesAsync(messages);
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }

        }

        /// <summary>
        /// receiveNewMsg（聚焦买家）和 onShopRobotReceriveNewMsgs（全部买家，见 ADR 0004）两条通道
        /// 共用的消息处理入口：先推送聊天记录，再逐条跑去重/进店提示过滤/消息合并。
        /// </summary>
        private async Task ProcessIncomingMessagesAsync(List<QNChatMessage> messages)
        {
            if (messages == null || messages.Count < 1)
            {
                return;
            }

            try
            {
                await PublishChatMessagesAsync(messages);
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                Log.Error("聊天记录推送失败，已跳过本次推送；千牛消息监听和自动回复将继续执行。");
            }

            foreach (var m in messages)
            {
                try
                {
                    await ProcessIncomingBuyerMessageAsync(m);
                }
                catch (Exception ex)
                {
                    Log.Exception(ex);
                }
            }
        }

        private async Task ProcessIncomingBuyerMessageAsync(QNChatMessage m)
        {
            if (m == null || m.fromid == null || m.toid == null || _seller == null)
            {
                return;
            }

            if (m.fromid.nick == _seller.Nick || m.toid.nick != _seller.Nick)
            {
                return;
            }

            if (!TryMarkIncomingMessageProcessed(_seller.Nick, m))
            {
                Log.Info(string.Format("[消息去重] 跳过重复买家消息，Seller={0}, Buyer={1}, ClientId={2}, MessageId={3}, SendTime={4}, Text={5}",
                    _seller.Nick,
                    m.fromid.nick,
                    m.mcode == null ? string.Empty : m.mcode.clientId,
                    m.mcode == null ? string.Empty : m.mcode.messageId,
                    m.sendTime,
                    m.summary));
                return;
            }

            if (!string.IsNullOrEmpty(m.fromid.targetId))
            {
                Log.Info(string.Format("[订单查询] 收到买家消息，尝试按买家查询订单。Buyer={0}, BuyerId={1}",
                    m.fromid.nick,
                    m.fromid.targetId));
                await GetBuyerTrades(m.fromid.targetId, string.Empty);
            }
            else
            {
                Log.Info(string.Format("[订单查询] 收到买家消息，但未携带买家ID。Buyer={0}", m.fromid.nick));
            }

            if (m.templateId == EntryIntoStoreTemplateId)
            {
                Log.Info(string.Format("[进店提示过滤] 跳过系统进店提示卡片，不计入AI对话历史。Buyer={0}, Summary={1}",
                    m.fromid.nick,
                    m.summary));
                return;
            }

            await HandleBuyerMessageWithCoalescingAsync(m);
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

        /// <summary>
        /// 按客服昵称查找已存在的 QN 实例，找不到返回 null（不会像 <see cref="GetByNick"/> 那样自动创建）。
        /// 供多买家汇总面板"确认"按钮按 SellerNick 找回对应账号用。
        /// </summary>
        public static QN FindBySellerNick(string sellerNick)
        {
            if (string.IsNullOrEmpty(sellerNick))
            {
                return null;
            }
            return QNSet.FirstOrDefault(q => q._seller != null && q._seller.Nick == sellerNick);
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
            var response = await cdp.GetBuyerTrades(securityBuyerUid, bizOrderId);
            Log.Info(string.Format(
                "[订单查询] 买家ID={0}, 请求订单号={1}, 返回数据={2}",
                securityBuyerUid,
                bizOrderId,
                JsonConvert.SerializeObject(response, Formatting.Indented)));
            return response;
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
