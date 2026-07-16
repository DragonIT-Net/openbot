using BotLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BotLib.Extensions;
using Bot.Common;

namespace Bot.Automation.ChatDeskNs.Automators
{
    public class QnAccountFinder
    {
        public virtual string ChatWindowTitlePattern
        {
            get
            {
                return "(千牛接待台|接待中心)";
            }
        }

        private static QnChatWnd currenrQNChatWnd;

        static QnAccountFinder()
        {
            currenrQNChatWnd = null;
        }


        private static HashSet<int> GetQianniuPids()
        {
            var pids = new HashSet<int>();
            var processNames = new[] { "AliWorkbench", "AliRender" };
            foreach (var processName in processNames)
            {
                var processes = Process.GetProcessesByName(processName);
                foreach (var p in processes.xSafeForEach())
                {
                    pids.Add(p.Id);
                }
            }
            return pids;
        }

        public virtual (QnChatWnd, QnChatWnd) GetSingleChatWnd()
        {
            var closedChatWnd = currenrQNChatWnd;
            var pids = GetQianniuPids();
            if (currenrQNChatWnd != null
                && pids.Contains(currenrQNChatWnd.Pid)
                && WinApi.IsHwndAlive(currenrQNChatWnd.Hwnd)
                && WinApi.IsVisible(currenrQNChatWnd.Hwnd))
            {
                return (currenrQNChatWnd,closedChatWnd);
            }
            currenrQNChatWnd = null;
            foreach (var pid in pids.xSafeForEach())
            {
                int hWnd;
                var title = GetWndTitle(pid, out hWnd);
                if (!string.IsNullOrEmpty(title) && hWnd != 0)
                {
                    var value = new QnChatWnd(title, hWnd ,pid);
                    currenrQNChatWnd = value;
                    break;
                }
            }
            return (currenrQNChatWnd, closedChatWnd);
        }

        private string GetWndTitle(int pid, out int hwnd)
		{
			var t = string.Empty;
			var htmp = 0;
			try
			{
			    WinApi.FindAllDesktopWindowByClassNameAndTitlePattern("Qt5152QWindowIcon", this.ChatWindowTitlePattern, (qnHwnd, title) =>
                {
                    if (WinApi.IsVisible(qnHwnd))
                    {
                        t = title;
                        htmp = qnHwnd;
                    }
                }, pid);
			}
			catch (Exception e)
			{
				Log.Exception(e);
			}
			hwnd = htmp;
			return t;
		}
    }
}
