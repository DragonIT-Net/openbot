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
        private long _sendTraceSeq;

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
            try
            {
                if (EnsureAutomationReady())
                {
                    UpdateChatBrowserRect();
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                automationApplication = null;
                uia3Automation = null;
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

        private static bool IsSendButtonName(string name)
        {
            return !string.IsNullOrEmpty(name)
                && name.Contains("发送")
                && !name.Contains("发送失败");
        }

        private static bool IsPrimarySendButtonName(string name)
        {
            return name == "发送";
        }

        private static bool IsReasonableSendContainer(AutomationElement element)
        {
            try
            {
                var rect = element.BoundingRectangle;
                return rect.Width >= 24
                    && rect.Height >= 18
                    && rect.Width <= 260
                    && rect.Height <= 120;
            }
            catch
            {
                return false;
            }
        }

        private static AutomationElement ResolveClickableSendElement(AutomationElement element)
        {
            if (element == null || !IsVisibleElement(element) || !IsSendButtonName(SafeName(element)))
            {
                return null;
            }

            var controlType = SafeControlType(element);
            if (controlType == ControlType.Button)
            {
                return element;
            }

            var current = element;
            for (var depth = 0; depth < 6 && current != null; depth++)
            {
                if (IsVisibleElement(current))
                {
                    var currentType = SafeControlType(current);
                    if (currentType == ControlType.Button)
                    {
                        return current;
                    }
                    if (depth > 0
                        && currentType != ControlType.Text
                        && IsReasonableSendContainer(current))
                    {
                        return current;
                    }
                }

                try
                {
                    current = current.Parent;
                }
                catch
                {
                    break;
                }
            }

            return controlType == ControlType.Text ? null : element;
        }

        private static string SafeElementText(AutomationElement element)
        {
            if (element == null)
            {
                return "<null>";
            }
            return string.Format("Name={0}, Class={1}, ControlType={2}, Offscreen={3}",
                SafeName(element),
                SafeClassName(element),
                SafeControlType(element),
                SafeIsOffscreen(element));
        }

        private static string SafeElementTextWithRect(AutomationElement element)
        {
            if (element == null)
            {
                return "<null>";
            }
            try
            {
                var rect = element.BoundingRectangle;
                return string.Format("{0}, Rect=({1},{2},{3},{4})",
                    SafeElementText(element),
                    rect.Left,
                    rect.Top,
                    rect.Width,
                    rect.Height);
            }
            catch
            {
                return SafeElementText(element);
            }
        }

        private static bool IsChatInputCandidate(AutomationElement element)
        {
            var name = SafeName(element);
            var cls = SafeClassName(element);
            if (name.Contains("搜索") || cls == "QLineEdit")
            {
                return false;
            }

            return cls == "TextRichEdit"
                || cls.IndexOf("RichEdit", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string PreviewText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            text = text.Replace("\r", "\\r").Replace("\n", "\\n");
            return text.Length > 80 ? text.Substring(0, 80) + "..." : text;
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
                    .OrderByDescending(w => SafeClassName(w) == "MutilChatView")
                    .ToList();

                if (candidateWindows.Count < 1)
                {
                    candidateWindows = topWnds.ToList();
                }

                AutomationElement sendButton = null;
                AutomationElement bestFallbackSendButton = null;
                AutomationElement inputTextArea = null;
                var loggedButtons = new List<string>();
                var loggedInputs = new List<string>();

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
                        foreach (var inputCandidate in descendants
                            .Where(k =>
                            {
                                var cls = SafeClassName(k);
                                return cls == "TextRichEdit"
                                    || cls.IndexOf("RichEdit", StringComparison.OrdinalIgnoreCase) >= 0
                                    || cls.IndexOf("Edit", StringComparison.OrdinalIgnoreCase) >= 0
                                    || SafeControlType(k) == ControlType.Edit;
                            }))
                        {
                            if (loggedInputs.Count < 20)
                            {
                                loggedInputs.Add(SafeElementTextWithRect(inputCandidate));
                            }
                            if (inputTextArea == null && IsChatInputCandidate(inputCandidate))
                            {
                                inputTextArea = inputCandidate;
                            }
                        }
                    }

                    AutomationElement primarySendButton = null;
                    AutomationElement fallbackSendButton = null;

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

                        var resolvedButton = ResolveClickableSendElement(element);
                        if (resolvedButton == null)
                        {
                            continue;
                        }

                        if (IsPrimarySendButtonName(name) || IsPrimarySendButtonName(SafeName(resolvedButton)))
                        {
                            primarySendButton = resolvedButton;
                            if (loggedButtons.Count < 30 && !object.ReferenceEquals(element, resolvedButton))
                            {
                                loggedButtons.Add("ResolvedPrimarySend=" + SafeElementText(resolvedButton));
                            }
                            break;
                        }

                        if (fallbackSendButton == null)
                        {
                            fallbackSendButton = resolvedButton;
                            if (loggedButtons.Count < 30 && !object.ReferenceEquals(element, resolvedButton))
                            {
                                loggedButtons.Add("ResolvedFallbackSend=" + SafeElementText(resolvedButton));
                            }
                        }
                    }

                    if (primarySendButton != null)
                    {
                        sendButton = primarySendButton;
                        break;
                    }

                    if (bestFallbackSendButton == null && fallbackSendButton != null)
                    {
                        bestFallbackSendButton = fallbackSendButton;
                    }
                }

                if (sendButton == null)
                {
                    sendButton = bestFallbackSendButton;
                }

                _sendMessageButton = sendButton;
                _messageInputTextArea = inputTextArea == null ? null : inputTextArea.AsTextBox();
                Log.Info("千牛输入框扫描结果：" + string.Join(" | ", loggedInputs));

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
            try
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
                Log.Info("千牛RPA初始化成功，ProcessId=" + desk.ProcessId);
                return true;
            }
            catch (Exception ex)
            {
                automationApplication = null;
                uia3Automation = null;
                Log.Exception(ex);
                Log.Error("千牛RPA初始化失败，消息监听不受影响；请确认千牛接待窗口已打开，后续发送时会重试初始化。");
                return false;
            }
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
                    if (_messageInputTextArea != null && IsChatInputCandidate(_messageInputTextArea))
                    {
                        var point = _messageInputTextArea.GetClickablePoint();
                        FlaUI.Core.Input.Mouse.Click(new System.Drawing.Point { X = point.X + 5, Y = point.Y + 5 });
                        isok = true;
                    }
                    else
                    {
                        Log.Error(string.Format("[发送诊断] FocusEditor取消：未找到聊天输入框。InputElement={0}",
                            SafeElementTextWithRect(_messageInputTextArea)));
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
            var traceId = string.Format("{0}-{1}", DateTime.Now.ToString("HHmmssfff"), Interlocked.Increment(ref _sendTraceSeq));
            await _sendLock.WaitAsync();
            try
            {
                Log.Info(string.Format("[发送诊断] TraceId={0}, Start, Buyer={1}, TextLength={2}, TextPreview={3}",
                    traceId,
                    buyer,
                    string.IsNullOrEmpty(text) ? 0 : text.Length,
                    PreviewText(text)));
                await OpenAndSendText(buyer, text, traceId);
            }
            finally
            {
                Log.Info(string.Format("[发送诊断] TraceId={0}, End", traceId));
                _sendLock.Release();
            }
        }

        public async Task PrepareTextAsync(string buyer, string text)
        {
            if (!EnsureAutomationReady())
            {
                return;
            }
            await _sendLock.WaitAsync();
            try
            {
                await OpenAndSetText(buyer, text);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task<bool> OpenAndSendText(string buyer, string text, string traceId)
        {
            Log.Info(string.Format("[发送诊断] TraceId={0}, Step=OpenAndSendText, Buyer={1}", traceId, buyer));
            if (!await OpenAndSetText(buyer, text, traceId))
            {
                Log.Error(string.Format("[发送诊断] TraceId={0}, Step=OpenAndSetText, Result=False", traceId));
                return false;
            }

            if (!TrySendByKeyboard(traceId))
            {
                Log.Error(string.Format("[发送诊断] TraceId={0}, 千牛自动发送未确认成功：已尝试 Enter 发送，未继续点击发送按钮，避免重复发送。", traceId));
                return false;
            }
            Log.Info(string.Format("[发送诊断] TraceId={0}, AutoSendResult=True", traceId));
            return true;
        }

        private async Task<bool> OpenAndSetText(string buyer, string text, string traceId = "")
        {
            bool sendResult = false;
            var desk = Desk.Inst;
            if (desk == null)
            {
                Log.Error(string.Format("[发送诊断] TraceId={0}, 未检测到千牛接待窗口，取消本次填写。", traceId));
                return false;
            }
            Log.Info(string.Format("[发送诊断] TraceId={0}, Step=BeforeOpenChat, DeskVisible={1}, DeskVisibleNotMinimized={2}, CurrentBuyer={3}, TargetBuyer={4}",
                traceId,
                desk.IsVisible,
                desk.IsVisibleAndNotMinimized,
                _qn.Buyer == null ? "<null>" : _qn.Buyer.Nick,
                buyer));
            if (_qn.Buyer == null || _qn.Buyer.Nick != buyer)
            {
                _qn.OpenChat(buyer);
                await Task.Delay(500);
                await _qn.GetCurrentConversationID();
                Log.Info(string.Format("[发送诊断] TraceId={0}, Step=AfterOpenChat, CurrentBuyer={1}",
                    traceId,
                    _qn.Buyer == null ? "<null>" : _qn.Buyer.Nick));
            }
            if (_qn.Buyer != null && _qn.Buyer.Nick == buyer)
            {
                if (!desk.IsVisible)
                {
                    desk.Show();
                    Util.WaitFor(new Func<bool>(() => desk.IsVisible), 3000, 10, false);
                }
                //SetAndSendText(text);

                var beforeEmpty = IsInputboxEmptySafe(traceId, "BeforeInsert");
                Log.Info(string.Format("[发送诊断] TraceId={0}, Step=BeforeInsert, InputEmpty={1}", traceId, beforeEmpty));
                _qn.InsertText2Inputbox(buyer, text);
                Thread.Sleep(500);
                var afterEmpty = IsInputboxEmptySafe(traceId, "AfterInsert");
                Log.Info(string.Format("[发送诊断] TraceId={0}, Step=AfterInsert, Buyer={1}, InputEmpty={2}", traceId, buyer, afterEmpty));
            }
            else
            {
                Log.Error(string.Format("[发送诊断] TraceId={0}, 未切换到买家会话，取消本次填写。CurrentBuyer={1}, TargetBuyer={2}",
                    traceId,
                    _qn.Buyer == null ? "<null>" : _qn.Buyer.Nick,
                    buyer));
                return false;
            }
            sendResult = true;
            return sendResult;
        }

        private bool TryClickSendButton(string traceId = "")
        {
            try
            {
                if (_sendMessageButton == null || !IsVisibleElement(_sendMessageButton))
                {
                    RefreshChatControls(true);
                }

                if (_sendMessageButton == null)
                {
                    Log.Error(string.Format("[发送诊断] TraceId={0}, Step=ClickButton, SendButton=<null>", traceId));
                    return false;
                }

                Log.Info(string.Format("[发送诊断] TraceId={0}, Step=ClickButton, SendButton={1}", traceId, SafeElementText(_sendMessageButton)));
                Desk.Inst.BringTop();
                try
                {
                    System.Drawing.Point point;
                    if (_sendMessageButton.TryGetClickablePoint(out point))
                    {
                        FlaUI.Core.Input.Mouse.Click(point);
                    }
                    else
                    {
                        var rect = _sendMessageButton.BoundingRectangle;
                        FlaUI.Core.Input.Mouse.Click(new System.Drawing.Point
                        {
                            X = rect.Left + rect.Width / 2,
                            Y = rect.Top + rect.Height / 2
                        });
                    }
                }
                catch
                {
                    _sendMessageButton.Click();
                }
                Thread.Sleep(800);
                if (IsInputboxEmptySafe(traceId, "AfterClickButton"))
                {
                    Log.Info(string.Format("[发送诊断] TraceId={0}, 已点击千牛发送按钮并确认输入框为空。", traceId));
                    return true;
                }

                Log.Error(string.Format("[发送诊断] TraceId={0}, 点击千牛发送按钮后输入框仍非空，Name={1}, ControlType={2}",
                    traceId,
                    SafeName(_sendMessageButton),
                    SafeControlType(_sendMessageButton)));
                return false;
            }
            catch (Exception e)
            {
                Log.Exception(e);
                _sendMessageButton = null;
                return false;
            }
        }

        private bool TrySendByKeyboard(string traceId = "")
        {
            try
            {
                var desk = Desk.Inst;
                if (desk == null)
                {
                    Log.Error(string.Format("[发送诊断] TraceId={0}, Step=KeyboardEnter, Desk=<null>", traceId));
                    return false;
                }

                desk.BringTop();
                var focusResult = FocusEditor();
                Log.Info(string.Format("[发送诊断] TraceId={0}, Step=KeyboardEnter, FocusEditor={1}, InputElement={2}",
                    traceId,
                    focusResult,
                    SafeElementText(_messageInputTextArea)));

                Log.Info(string.Format("[发送诊断] TraceId={0}, Step=KeyboardEnter, Action=PressEnterKey", traceId));
                WinApi.PressEnterKey();
                if (WaitInputboxEmptyAfterSend(traceId, "AfterEnter"))
                {
                    Log.Info(string.Format("[发送诊断] TraceId={0}, 已通过 Enter 发送千牛消息并确认输入框为空。", traceId));
                    return true;
                }
                Log.Error(string.Format("[发送诊断] TraceId={0}, 通过 Enter 尝试发送后输入框仍非空。", traceId));
                return false;
            }
            catch (Exception e)
            {
                Log.Exception(e);
                return false;
            }
        }

        private bool WaitInputboxEmptyAfterSend(string traceId, string stage)
        {
            var delays = new[] { 200, 800, 1500 };
            foreach (var delay in delays)
            {
                Thread.Sleep(delay);
                var empty = IsInputboxEmptySafe(traceId, stage + "+" + delay + "ms");
                Log.Info(string.Format("[输入框诊断] TraceId={0}, Stage={1}, DelayMs={2}, IsEmpty={3}",
                    traceId,
                    stage,
                    delay,
                    empty));
                if (empty)
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsInputboxEmptySafe(string traceId = "", string stage = "")
        {
            try
            {
                var task = _qn.IsInputboxEmpty();
                if (task.Wait(1200))
                {
                    Log.Info(string.Format("[输入框诊断] TraceId={0}, Stage={1}, IsEmpty={2}", traceId, stage, task.Result));
                    return task.Result;
                }
                Log.Error(string.Format("[输入框诊断] TraceId={0}, Stage={1}, 检测千牛输入框是否为空超时。", traceId, stage));
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
