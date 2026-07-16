using System;
using BotLib;
using SuperWebSocket;
using Newtonsoft.Json;
using Bot.ChromeNs;
using Bot.Automation;
using Bot.Automation.ChatDeskNs;
using SuperSocket.SocketBase.Config;
using Bot.AssistWindow.NotifyIcon;
using System.Threading.Tasks;

namespace Bot.ChromeNs
{
    public class MyWebSocketServer
    {
        public static MyWebSocketServer WSocketSvrInst = null;
        static MyWebSocketServer()
        {
            if (WSocketSvrInst == null) WSocketSvrInst = new MyWebSocketServer();
        }

        public EventHandler<WSocketNewMessageEventArgs> OnRecieveMessage;

        public void Start()
        {
            try
            {
                var webSocket = new WebSocketServer();
                webSocket.NewSessionConnected += async (session) =>
                {
                    var cdp = new CDPClient(session);
                    var user = await cdp.GetCurrentUser();
                    var ver = await cdp.GetVersion();
                    if (user == null || user.Result == null)
                    {
                        Log.Error("千牛页面已连接，但未获取到当前登录用户。");
                        return;
                    }
                    await WaitForDeskReady();
                    QN qn = QN.GetByNick(user.Result);
                    qn.QnVersion = ver == null ? string.Empty : ver.version;
                    qn.CDP = cdp;
                    WndNotifyIcon.Inst.AddSellerMenuItem(qn.Seller.Nick);
                };
                webSocket.NewMessageReceived += (session, value) =>
                {
                    var wMsg = JsonConvert.DeserializeObject<WSocketMessage>(value);
                    if (wMsg.Type == "hi") return;

                    if (OnRecieveMessage != null)
                        OnRecieveMessage(session, new WSocketNewMessageEventArgs(wMsg.Type,wMsg.Response));
                };
                webSocket.SessionClosed += (session, value) =>
                {
                    Console.Write(value);
                };
                var config = new ServerConfig()
                {
                    MaxRequestLength = 5* 1024 * 1024,
                    Ip = "127.0.0.1",
                    Port = 41010
                };
                webSocket.Setup(config);//设置端口
                webSocket.Start();
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
            finally
            {

            }
        }

        private async Task<bool> WaitForDeskReady()
        {
            var endTime = DateTime.Now.AddSeconds(10);
            while (Desk.Inst == null && DateTime.Now < endTime)
            {
                await Task.Delay(200);
            }

            if (Desk.Inst == null)
            {
                Log.Error("千牛页面已连接，但尚未检测到千牛接待窗口。");
                return false;
            }

            return true;
        }
    }

    public class WSocketMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("response")]
        public string Response { get; set; }
    }

    public class WSocketNewMessageEventArgs : EventArgs
    {
        public string Type { get; private set; }
        public string Value { get; private set; }

        public WSocketNewMessageEventArgs(string type, string value)
        {
            Type = type;
            Value = value;
        }
    }

}
