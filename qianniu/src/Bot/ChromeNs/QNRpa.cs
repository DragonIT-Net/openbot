using BotLib.Extensions;
using BotLib.Wpf.Extensions;
using BotLib;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Bot.Automation.ChatDeskNs;
using System.Windows;
using Bot.Automation;

namespace Bot.ChromeNs
{
    public class QNRpa
    {

        private DateTime _preUpdateChatBrowserRectTime;
        private DateTime _preSendPlainTextAndImageTime;
        private BitmapImage _preSendPlainTextAndImageImage;
        public DateTime LatestSetTextTime;

        private AutomationElement _sendMessageButton;
        private AutomationElement _closeContactButton;
        private TextBox _messageInputTextArea;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        private FlaUI.Core.Application automationApplication;
        private UIA3Automation uia3Automation;

        public string LastSetPlainText
        {
            get;
            private set;
        }

        private QN _qn;

        public QNRpa(QN qn)
        {
            _qn = qn;
            if (EnsureAutomationReady())
            {
                UpdateChatBrowserRect();
            }
        }


        public async void UpdateChatBrowserRect()
        {
            if (!EnsureAutomationReady())
            {
                return;
            }

            if (Desk.Inst.IsVisibleAndNotMinimized)
            {
                if ((DateTime.Now - _preUpdateChatBrowserRectTime).TotalSeconds >= 3)
                {
                    _preUpdateChatBrowserRectTime = DateTime.Now;
                    await Task.Run(() =>
                    {
                        RefreshChatControls(true);
                    });

                }
            }


        }

        private static string SafeName(AutomationElement element)
        {
            try
            {
                return element != null && element.Properties.Name.IsSupported ? element.Name : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SafeClassName(AutomationElement element)
        {
            try
            {
                return element != null && element.Properties.ClassName.IsSupported ? element.ClassName : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsVisibleElement(AutomationElement element)
        {
            try
            {
                return element != null && !element.IsOffscreen;
            }
            catch
            {
                return element != null;
            }
        }

        private static ControlType SafeControlType(AutomationElement element)
        {
            try
            {
                return element == null ? ControlType.Unknown : element.ControlType;
            }
            catch
            {
                return ControlType.Unknown;
            }
        }

        private static bool SafeIsOffscreen(AutomationElement element)
        {
            try
            {
                return element != null && element.IsOffscreen;
            }
            catch
            {
                return false;
            }
        }

        private bool RefreshChatControls(bool force = false)
        {
            try
            {
                if (!EnsureAutomationReady())
                {
                    return false;
                }

                if (!force
                    && _sendMessageButton != null
                    && IsVisibleElement(_sendMessageButton))
                {
                    return true;
                }

                var topWnds = automationApplication.GetAllTopLevelWindows(uia3Automation);
                var candidateWindows = topWnds
                    .Where(w =>
                    {
                        var name = SafeName(w);
                        var cls = SafeClassName(w);
                        return cls == "MutilChatView"
                            || name.Contains("接待")
                            || name.Contains("千牛")
                            || name.Contains("工作台");
                    })
                    .ToList();

                if (candidateWindows.Count < 1)
                {
                    candidateWindows = topWnds.ToList();
                }

                AutomationElement sendButton = null;
                AutomationElement inputTextArea = null;
                var loggedButtons = new List<string>();

                foreach (var wnd in candidateWindows)
                {
                    AutomationElement[] descendants;
                    try
                    {
                        descendants = wnd.FindAllDescendants();
                    }
                    catch
                    {
                        continue;
                    }

                    if (inputTextArea == null)
                    {
                        inputTextArea = descendants.FirstOrDefault(k =>
                        {
                            var cls = SafeClassName(k);
                            return cls == "TextRichEdit"
                                || cls.IndexOf("RichEdit", StringComparison.OrdinalIgnoreCase) >= 0
                                || cls.IndexOf("Edit", StringComparison.OrdinalIgnoreCase) >= 0;
                        });
                    }

                    foreach (var element in descendants)
                    {
                        var name = SafeName(element);
                        var cls = SafeClassName(element);
                        var controlType = SafeControlType(element);
                        if (controlType == ControlType.Button || name.Contains("发送"))
                        {
                            if (loggedButtons.Count < 30)
                            {
                                loggedButtons.Add(string.Format("Name={0}, Class={1}, ControlType={2}, Offscreen={3}",
                                    name,
                                    cls,
                                    controlType,
                                    SafeIsOffscreen(element)));
                            }
                        }

                        if (sendButton == null
                            && IsVisibleElement(element)
                            && name.Contains("发送")
                            && !name.Contains("发送失败"))
                        {
                            sendButton = element;
                        }
                    }

                    if (sendButton != null)
                    {
                        break;
                    }
                }

                _sendMessageButton = sendButton;
                _messageInputTextArea = inputTextArea == null ? null : inputTextArea.AsTextBox();

                if (_sendMessageButton == null)
                {
                    Log.Info("千牛发送按钮扫描结果：" + string.Join(" | ", loggedButtons));
                }
                else
                {
                    Log.Info(string.Format("已定位千牛发送按钮：Name={0}, Class={1}, ControlType={2}",
                        SafeName(_sendMessageButton),
                        SafeClassName(_sendMessageButton),
                        SafeControlType(_sendMessageButton)));
                }

                return _sendMessageButton != null;
            }
            catch (Exception e)
            {
                Log.Exception(e);
                return false;
            }
        }

        private bool EnsureAutomationReady()
        {
            if (automationApplication != null && uia3Automation != null)
            {
                return true;
            }

            var desk = Desk.Inst;
            if (desk == null)
            {
                Log.Error("未检测到千牛接待窗口，暂不初始化RPA。请打开千牛接待台后重试。");
                return false;
            }

            automationApplication = FlaUI.Core.Application.Attach(desk.ProcessId);
            uia3Automation = new UIA3Automation();
            return true;
        }

        public async Task SendImageAsync(string buyer, string imagePath)
        {
            if (!EnsureAutomationReady())
            {
                return;
            }
            await Task.Run(() =>
            {
                var image = BitmapImageEx.CreateFromFile(imagePath);
                OpenAndSendImage(buyer, image);
            });
        }

        private bool OpenAndSendImage(string buyer, BitmapImage image)
        {
            bool sendResult = false;
            if (_qn.Buyer == null || _qn.Buyer.Nick != buyer)
            {
                _qn.OpenChat(buyer);
                Thread.Sleep(500);
                Util.WaitFor(() => _qn.Buyer.Nick == buyer, 5000, 10, false);
            }
            if (_qn.Buyer.Nick == buyer)
            {
                if (!Desk.Inst.IsVisible)
                {
                    Desk.Inst.Show();
                    Util.WaitFor(new Func<bool>(() => Desk.Inst.IsVisible), 3000, 10, false);
                }
                SetAndSendImage(image);
            }
            sendResult = true;
            return sendResult;
        }

        private bool SetAndSendImage(BitmapImage image)
        {
            bool rt = false;
            if ((DateTime.Now - _preSendPlainTextAndImageTime).TotalSeconds < 1.1
                && _preSendPlainTextAndImageImage == image)
            {
                rt = false;
            }
            else
            {
                _preSendPlainTextAndImageTime = DateTime.Now;
                _preSendPlainTextAndImageImage = image;
                if (SetImage(image))
                {
                    if (!TryClickSendButton())
                    {
                        rt = TrySendByKeyboard();
                    }
                    else
                    {
                        rt = true;
                    }
                }
                else
                {
                    rt = false;
                }
            }
            return rt;
        }

        private bool SetImage(BitmapImage img)
        {
            bool isok = false;
            ClipboardEx.UseClipboardWithAutoRestoreInUiThread(() =>
            {
                FocusEditor();
                Clipboard.Clear();
                Clipboard.SetImage(img);
                WinApi.PressCtrlV();
                DateTime now = DateTime.Now;
                do
                {
                    if (_messageInputTextArea != null && !string.IsNullOrEmpty(_messageInputTextArea.Text))
                    {
                        isok = true;
                        break;
                    }
                    DispatcherEx.DoEvents();
                } while ((DateTime.Now - now).TotalSeconds < 2.0);
                Util.WriteTimeElapsed(now, "等待时间");
            });
            return isok;
        }

        public bool FocusEditor()
        {
            bool isok = false;
            DispatcherEx.xInvoke(() =>
            {
                Desk.Inst.BringTop();
                try
                {
                    if (_messageInputTextArea != null)
                    {
                        var point = _messageInputTextArea.GetClickablePoint();
                        FlaUI.Core.Input.Mouse.Click(new System.Drawing.Point { X = point.X + 5, Y = point.Y + 5 });
                        isok = true;
                    }
                }
                catch (Exception e)
                {
                    Log.Exception(e);
                }
            });
            return isok;
        }

        public async Task SendTextAsync(string buyer, string text)
        {
            if (!EnsureAutomationReady())
            {
                return;
            }
            await _sendLock.WaitAsync();
            try
            {
                await OpenAndSendText(buyer, text);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task<bool> OpenAndSendText(string buyer, string text)
        {
            bool sendResult = false;
            var desk = Desk.Inst;
            if (desk == null)
            {
                Log.Error("未检测到千牛接待窗口，取消本次自动发送。");
                return false;
            }
            if (_qn.Buyer == null || _qn.Buyer.Nick != buyer)
            {
                _qn.OpenChat(buyer);
                await Task.Delay(500);
                await _qn.GetCurrentConversationID();
            }
            if (_qn.Buyer != null && _qn.Buyer.Nick == buyer)
            {
                if (!desk.IsVisible)
                {
                    desk.Show();
                    Util.WaitFor(new Func<bool>(() => desk.IsVisible), 3000, 10, false);
                }
                //SetAndSendText(text);

                _qn.InsertText2Inputbox(buyer, text);
                Thread.Sleep(500);
                if (!TryClickSendButton())
                {
                    Log.Error("未找到千牛发送按钮，尝试使用键盘快捷键发送。");
                    if (!TrySendByKeyboard())
                    {
                        Log.Error("千牛自动发送失败：按钮和键盘快捷键均未发送成功。");
                        return false;
                    }
                }
            }
            else
            {
                Log.Error("未切换到买家会话，取消本次自动发送。");
                return false;
            }
            sendResult = true;
            return sendResult;
        }

        private bool TryClickSendButton()
        {
            try
            {
                if (_sendMessageButton == null || !IsVisibleElement(_sendMessageButton))
                {
                    RefreshChatControls(true);
                }

                if (_sendMessageButton == null)
                {
                    return false;
                }

                _sendMessageButton.Click();
                return true;
            }
            catch (Exception e)
            {
                Log.Exception(e);
                _sendMessageButton = null;
                return false;
            }
        }

        private bool TrySendByKeyboard()
        {
            try
            {
                var desk = Desk.Inst;
                if (desk == null)
                {
                    return false;
                }

                desk.BringTop();
                FocusEditor();

                WinApi.PressEnterKey();
                Thread.Sleep(600);
                Log.Info("已通过 Enter 尝试发送千牛消息。");
                return true;
            }
            catch (Exception e)
            {
                Log.Exception(e);
                return false;
            }
        }

        private bool IsInputboxEmptySafe()
        {
            try
            {
                var task = _qn.IsInputboxEmpty();
                if (task.Wait(1200))
                {
                    return task.Result;
                }
                Log.Error("检测千牛输入框是否为空超时。");
                return false;
            }
            catch (Exception e)
            {
                Log.Exception(e);
                return false;
            }
        }


        private bool SetAndSendText(string text)
        {
            var isok = false;
            try
            {
                //_messageInputTextArea.Text = text;
                //await Task.Delay(200);

                //ClipboardEx.UseClipboardWithAutoRestoreInUiThread(() =>
                //{
                //    FocusEditor();
                //    Clipboard.Clear();
                //    ClipboardEx.SetTextSafe(text);
                //    WinApi.PressCtrlV();
                //    DateTime now = DateTime.Now;
                //    do
                //    {
                //        if (!_qn.IsInputboxEmpty().GetAwaiter().GetResult())
                //        {
                //            isok = true;
                //            break;
                //        }
                //        DispatcherEx.DoEvents();
                //    } while ((DateTime.Now - now).TotalSeconds < 2.0);
                //    Util.WriteTimeElapsed(now, "等待时间");
                //});
                //var point = _sendMessageButton.GetClickablePoint();

                //_qn.InsertText2Inputbox(text);

                //_sendMessageButton.Click();
                //WinApi.PressEnter();
            }
            catch (Exception e)
            {
                Log.Exception(e);
            }
            return isok;
        }

    }
}
