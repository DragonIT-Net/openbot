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
using System.Diagnostics;

namespace Bot.ChromeNs
{
    public class MyWebSocketServer
    {
        public static MyWebSocketServer WSocketSvrInst = null;
        private WebSocketServer _webSocket;
        private readonly object _startLock = new object();
        static MyWebSocketServer()
        {
            if (WSocketSvrInst == null) WSocketSvrInst = new MyWebSocketServer();
        }

        public EventHandler<WSocketNewMessageEventArgs> OnRecieveMessage;

        public void Start()
        {
            try
            {
                lock (_startLock)
                {
                    if (_webSocket != null)
                    {
                        Log.Info("[WebSocket服务] 已启动，跳过重复启动。");
                        return;
                    }

                    _webSocket = new WebSocketServer();
                    _webSocket.NewSessionConnected += async (session) =>
                    {
                        try
                        {
                            Log.Info("[WebSocket连接] 千牛页面已连接，Session=" + (session == null ? string.Empty : session.SessionID));
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
                            Log.Info(string.Format("[WebSocket连接] 千牛用户绑定成功，Seller={0}, QnVersion={1}",
                                qn.Seller == null ? string.Empty : qn.Seller.Nick,
                                qn.QnVersion));
                        }
                        catch (Exception ex)
                        {
                            Log.Exception(ex);
                        }
                    };
                    _webSocket.NewMessageReceived += (session, value) =>
                    {
                        try
                        {
                            var wMsg = JsonConvert.DeserializeObject<WSocketMessage>(value);
                            if (wMsg == null)
                            {
                                Log.Error("[WebSocket事件] 反序列化为空，RawLength=" + (value == null ? 0 : value.Length));
                                return;
                            }
                            if (wMsg.Type == "hi") return;
                            Log.Info(string.Format("[WebSocket事件] Session={0}, Type={1}, ResponseLength={2}",
                                session == null ? string.Empty : session.SessionID,
                                wMsg.Type,
                                wMsg.Response == null ? 0 : wMsg.Response.Length));

                            if (OnRecieveMessage != null)
                                OnRecieveMessage(session, new WSocketNewMessageEventArgs(wMsg.Type, wMsg.Response, wMsg.Error));
                        }
                        catch (Exception ex)
                        {
                            Log.Exception(ex);
                        }
                    };
                    _webSocket.SessionClosed += (session, value) =>
                    {
                        Log.Info(string.Format("[WebSocket连接] Session关闭，Session={0}, Reason={1}",
                            session == null ? string.Empty : session.SessionID,
                            value));
                    };
                    var config = new ServerConfig()
                    {
                        MaxRequestLength = 5 * 1024 * 1024,
                        Ip = "127.0.0.1",
                        Port = 41010
                    };
                    Log.Info(string.Format("[WebSocket服务] 准备启动，Ip={0}, Port={1}", config.Ip, config.Port));
                    var setupOk = _webSocket.Setup(config);//设置端口
                    Log.Info("[WebSocket服务] Setup结果=" + setupOk);
                    var startOk = setupOk && _webSocket.Start();
                    Log.Info("[WebSocket服务] Start结果=" + startOk);
                    if (!startOk)
                    {
                        _webSocket = null;
                        Log.Error("[WebSocket服务] 启动失败，请检查41010端口是否被占用，或是否已有程序实例在运行。");
                    }
                }
            }
            catch (Exception ex)
            {
                _webSocket = null;
                Log.Exception(ex);
            }
            finally
            {

            }
        }

        private async Task<bool> WaitForDeskReady()
        {
            var endTime = DateTime.Now.AddSeconds(10);
            while (!IsDeskReady() && DateTime.Now < endTime)
            {
                await Task.Delay(200);
            }

            if (!IsDeskReady())
            {
                Log.Error("千牛页面已连接，但尚未检测到千牛接待窗口。");
                return false;
            }

            return true;
        }

        private static bool IsDeskReady()
        {
            var desk = Desk.Inst;
            if (desk == null)
            {
                return false;
            }

            try
            {
                var process = Process.GetProcessById(desk.ProcessId);
                return process != null && !process.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }

    public class WSocketMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("response")]
        public string Response { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }
    }

    public class WSocketNewMessageEventArgs : EventArgs
    {
        public string Type { get; private set; }
        public string Value { get; private set; }
        public string Error { get; private set; }

        public WSocketNewMessageEventArgs(string type, string value, string error = null)
        {
            Type = type;
            Value = value;
            Error = error;
        }
    }

}
