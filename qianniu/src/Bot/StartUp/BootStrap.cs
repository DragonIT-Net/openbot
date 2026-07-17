using BotLib;
using BotLib.Extensions;
using Bot.ControllerNs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bot.Common.Db;
using Bot.Common;
using BotLib.Wpf.Extensions;
using Bot.ChromeNs;

namespace Bot
{
    public class BootStrap
    {
        public static void Init()
        {
            Log.Info("[启动诊断] Init开始。");
            ClearTmpPathFiles();
            Log.Info("[启动诊断] 临时目录清理完成。");
            DeskScanner.LoopScan();
            Log.Info("[启动诊断] 千牛窗口扫描已启动。");
            MyWebSocketServer.WSocketSvrInst.Start();
            Log.Info("[启动诊断] WebSocket服务启动流程已执行。");
            QNInject.StartInject();
            Log.Info("[启动诊断] 千牛注入检查流程已触发。");

            //var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"inject.js"));
            //IseiyaHttpProxy.StartProxy(script);
        }

        private static void ClearTmpPathFiles()
        {
            try
            {
                if (Directory.GetFiles(PathEx.TmpPath).Length > 0)
                {
                    DirectoryEx.Delete(PathEx.TmpPath, true);
                    Directory.CreateDirectory(PathEx.TmpPath);
                }
            }
            catch (Exception e)
            {
                Log.Exception(e);
            }
        }
    }
}
