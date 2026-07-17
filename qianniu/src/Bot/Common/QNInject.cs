using IniParser.Model;
using IniParser;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Zip;
using BotLib;
using System.Windows;
using Newtonsoft.Json;

namespace Bot.Common
{
    public class QNInject
    {
        private const string processName = "AliWorkbench";
        private const string webuiResDir = "newWebui";
        private const string webuiFile = "webui.zip";
        private const string signFile = "sign.json";
        private const string chatRecentHtmlFile = @"web_chat-packer/recent.html";
        private const string imSupportUrl = @"https://iseiya.taobao.com/imsupport";
        private const string overWriteUrl = "https://worklink.oss-cn-hangzhou.aliyuncs.com/5CFB5E11D17E63CDD8CB37B52FA6ACFD.js"; 
        private const string localWsUrl = "ws://127.0.0.1:41010";
        private const string injectMarker = "___setupWebSocket";


        public static async Task StartInject()
        {
            try
            {
                Log.Info("[注入诊断] 开始检查千牛注入状态。");
                string installPath = FindInstallPath();
                Log.Info("[注入诊断] 千牛安装目录=" + (installPath ?? string.Empty));
                if (string.IsNullOrEmpty(installPath))
                {
                    MessageBox.Show("没有检测到安装的千牛!!");
                    Log.Error("[注入诊断] 未检测到千牛安装目录。");
                }

                var resourcePath = FindResourcePath(installPath);
                Log.Info("[注入诊断] 千牛资源目录=" + (resourcePath ?? string.Empty));
                if (string.IsNullOrEmpty(resourcePath))
                {
                    MessageBox.Show("获取千牛资源目录失败!!");
                    Log.Error("[注入诊断] 获取千牛资源目录失败。");
                    return;
                }

                if (IsInjected(resourcePath))
                {
                    Log.Info("[注入诊断] 千牛插件已注入，无需重复注入。");
                    return;
                }

                if (IsWorkbenchRunning())
                {
                    Log.Info("[注入诊断] 检测到千牛正在运行，需要退出后注入。");
                    if (MessageBox.Show("请先退出千牛，在运行此程序!!", "提示", MessageBoxButton.YesNo)
                        == MessageBoxResult.No)
                    {
                        Log.Info("[注入诊断] 用户取消关闭千牛，跳过注入。");
                        return;
                    }
                    else {
                        foreach (var p in Process.GetProcessesByName(processName))
                        {
                            p.Kill();
                        }
                    }
                    await Task.Delay(2000);
                }


                if (InjectScript(resourcePath))
                {
                    Log.Info("[注入诊断] 千牛插件注入成功。");
                    MessageBox.Show("千牛插件注入成功，请重新启动千牛!!");
                }
                else
                {
                    Log.Error("[注入诊断] 千牛插件注入失败。");
                    MessageBox.Show("千牛插件注入失败!!");
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        }

        private static string FindInstallPath()
        {
            var candidates = new List<string>();
            var configuredPath = Params.Robot.GetQianNiuInstallPath();
            if (!string.IsNullOrEmpty(configuredPath))
            {
                configuredPath = configuredPath.Trim();
                Log.Info("[注入诊断] 已读取手动配置千牛安装目录=" + configuredPath);
                string configuredResourcePath;
                if (TryResolveResourcePath(configuredPath, out configuredResourcePath))
                {
                    Log.Info("[注入诊断] 手动配置目录校验通过，资源目录=" + configuredResourcePath);
                    return configuredPath;
                }

                Log.Error("[注入诊断] 手动配置千牛安装目录无效，已停止自动扫描。请检查目录是否包含 Resources\\newWebui\\webui.zip。Path=" + configuredPath);
                return configuredPath;
            }

            try
            {
                RegistryKey registryKey = Registry.ClassesRoot.OpenSubKey("aliim");
                if (registryKey != null)
                {
                    registryKey = registryKey.OpenSubKey("Shell");
                }
                if (registryKey != null)
                {
                    registryKey = registryKey.OpenSubKey("Open");
                }
                if (registryKey != null)
                {
                    registryKey = registryKey.OpenSubKey("Command");
                }
                if (registryKey != null)
                {
                    var command = registryKey.GetValue("") == null ? string.Empty : registryKey.GetValue("").ToString();
                    AddInstallPathCandidate(candidates, ExtractExecutablePath(command));
                }
            }
            catch (Exception e)
            {
                Log.Exception(e, "installPath");
            }

            try
            {
                foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName)))
                {
                    try
                    {
                        AddInstallPathCandidate(candidates, process.MainModule == null ? string.Empty : process.MainModule.FileName);
                    }
                    catch (Exception ex)
                    {
                        Log.Exception(ex);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Exception(e, "processPath");
            }

            var installPath = candidates
                .Where(path => !string.IsNullOrEmpty(path) && Directory.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(path => !string.IsNullOrEmpty(FindResourcePath(path)));
            if (string.IsNullOrEmpty(installPath))
            {
                installPath = candidates
                    .Where(path => !string.IsNullOrEmpty(path) && Directory.Exists(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
            }
            Log.Info("[注入诊断] 千牛安装目录候选=" + string.Join(" | ", candidates.Distinct(StringComparer.OrdinalIgnoreCase)));
            return installPath;
        }

        private static string ExtractExecutablePath(string command)
        {
            if (string.IsNullOrEmpty(command))
            {
                return string.Empty;
            }

            command = command.Trim();
            if (command.StartsWith("\""))
            {
                var end = command.IndexOf("\"", 1, StringComparison.Ordinal);
                if (end > 1)
                {
                    return command.Substring(1, end - 1);
                }
            }

            var exeIndex = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exeIndex < 0 ? command : command.Substring(0, exeIndex + 4);
        }

        private static void AddInstallPathCandidate(List<string> candidates, string executablePath)
        {
            if (string.IsNullOrEmpty(executablePath))
            {
                return;
            }

            try
            {
                if (!File.Exists(executablePath))
                {
                    return;
                }

                var fileName = Path.GetFileName(executablePath);
                var dir = Directory.GetParent(executablePath);
                if (dir == null)
                {
                    return;
                }

                if (string.Equals(fileName, "wwcmd.exe", StringComparison.OrdinalIgnoreCase)
                    && dir.Parent != null)
                {
                    candidates.Add(dir.Parent.FullName);
                }
                candidates.Add(dir.FullName);
            }
            catch (Exception e)
            {
                Log.Exception(e);
            }
        }

        private static string FindResourcePath(string installPath)
        {
            if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath))
            {
                return string.Empty;
            }

            var aliWorkbenchConfigPath = Path.Combine(installPath, "AliWorkbench.ini");
            if (File.Exists(aliWorkbenchConfigPath))
            {
                var version = ReadAliConfigFile(aliWorkbenchConfigPath);
                var resourcePath = Path.Combine(installPath, version, "Resources");
                if (IsValidResourcePath(resourcePath))
                {
                    return resourcePath;
                }
                Log.Error(string.Format("[注入诊断] AliWorkbench.ini 指向的资源目录无效，Version={0}, ResourcePath={1}", version, resourcePath));
            }

            var directResourcePath = Path.Combine(installPath, "Resources");
            if (IsValidResourcePath(directResourcePath))
            {
                return directResourcePath;
            }

            try
            {
                foreach (var dir in Directory.GetDirectories(installPath)
                    .OrderByDescending(path => Directory.GetLastWriteTime(path)))
                {
                    var resourcePath = Path.Combine(dir, "Resources");
                    if (IsValidResourcePath(resourcePath))
                    {
                        Log.Info("[注入诊断] 通过兜底扫描找到千牛资源目录：" + resourcePath);
                        return resourcePath;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Exception(e);
            }

            return string.Empty;
        }

        public static bool TryResolveResourcePath(string installPath, out string resourcePath)
        {
            resourcePath = FindResourcePath(installPath);
            return !string.IsNullOrEmpty(resourcePath);
        }

        private static bool IsValidResourcePath(string resourcePath)
        {
            return !string.IsNullOrEmpty(resourcePath)
                && File.Exists(Path.Combine(resourcePath, webuiResDir, webuiFile));
        }


        private static bool IsWorkbenchRunning()
        {
            var processes = Process.GetProcessesByName(processName);
            return processes.Length > 0;
        }

        public static bool IsInjected(string resourcePath)
        {
            var webuiResPath = Path.Combine(resourcePath, webuiResDir, webuiFile);
            using (var zipFile = new ZipFile(webuiResPath))
            {
                var entry = zipFile.GetEntry(chatRecentHtmlFile);
                using (var inputStream = zipFile.GetInputStream(entry))
                {
                    using (var streamReader = new StreamReader(inputStream))
                    {
                        var chatRecentHtmlContent = streamReader.ReadToEnd();
                        var hasLocalSocket = chatRecentHtmlContent.Contains(localWsUrl);
                        var hasMarker = chatRecentHtmlContent.Contains(injectMarker);
                        Log.Info(string.Format("[注入诊断] 注入内容检查：HasLocalSocket={0}, HasMarker={1}, HasRemoteWorklink={2}, HasOriginalImSupport={3}",
                            hasLocalSocket,
                            hasMarker,
                            chatRecentHtmlContent.Contains(overWriteUrl),
                            chatRecentHtmlContent.Contains(imSupportUrl)));
                        return hasLocalSocket && hasMarker;
                    }
                }
            }
        }

        private static bool InjectScript(string resourcePath)
        {
            var webuiResPath = Path.Combine(resourcePath, webuiResDir,webuiFile);
            var signPath = Path.Combine(resourcePath, webuiResDir, signFile);
            var injectScript = ReadInjectScript();

            using (var zipFile = new ZipFile(webuiResPath))
            {
                var entry = zipFile.GetEntry(chatRecentHtmlFile);
                using (var inputStream = zipFile.GetInputStream(entry))
                {
                    using (var streamReader = new StreamReader(inputStream))
                    {
                        var chatRecentHtmlContent = streamReader.ReadToEnd();
                        if (chatRecentHtmlContent.Contains(localWsUrl) && chatRecentHtmlContent.Contains(injectMarker))
                        {
                            return true;
                        }

                        var beforeContent = chatRecentHtmlContent;
                        var inlineScriptAssignment = "script.text = " + JsonConvert.SerializeObject(injectScript) + ";";
                        chatRecentHtmlContent = chatRecentHtmlContent.Replace(
                            "script.src = \"" + imSupportUrl + "\";",
                            inlineScriptAssignment);
                        chatRecentHtmlContent = chatRecentHtmlContent.Replace(
                            "script.src = \"" + overWriteUrl + "\";",
                            inlineScriptAssignment);

                        if (beforeContent == chatRecentHtmlContent)
                        {
                            var inlineScriptTag = "<script>\r\n" + injectScript.Replace("</script>", "<\\/script>") + "\r\n</script>";
                            if (chatRecentHtmlContent.Contains("</body>"))
                            {
                                chatRecentHtmlContent = chatRecentHtmlContent.Replace("</body>", inlineScriptTag + "\r\n  </body>");
                            }
                            else
                            {
                                chatRecentHtmlContent += "\r\n" + inlineScriptTag;
                            }
                            Log.Info("[注入诊断] 未找到原始脚本地址，已使用追加方式写入本地监听脚本。");
                        }
                        else
                        {
                            Log.Info("[注入诊断] 已将远程脚本替换为本地监听脚本。");
                        }

                        zipFile.BeginUpdate();
                        zipFile.Add(new ZipStaticDataSource(chatRecentHtmlContent), chatRecentHtmlFile);
                        zipFile.CommitUpdate();
                        if (File.Exists(signPath))
                        {
                            var signFi = new FileInfo(signPath);
                            if (signFi.Length > 0)
                            {
                                signFi.Delete();
                                using (File.Create(signPath))
                                {
                                }
                            }
                        }
                        return true;
                    }
                }
            }

        }

        private static string ReadInjectScript()
        {
            var injectPath = Path.Combine(AppContext.BaseDirectory, "inject.js");
            if (!File.Exists(injectPath))
            {
                throw new FileNotFoundException("未找到注入脚本 inject.js", injectPath);
            }

            var injectScript = File.ReadAllText(injectPath, Encoding.UTF8);
            if (!injectScript.Contains(localWsUrl) || !injectScript.Contains(injectMarker))
            {
                throw new InvalidDataException("inject.js 缺少本地 WebSocket 监听脚本。");
            }
            Log.Info("[注入诊断] 已读取本地注入脚本：" + injectPath);
            return injectScript;
        }

        private static string ReadAliConfigFile(string path)
        {
            var parser = new FileIniDataParser();
            var data = parser.ReadFile(path);
            string version = data["Common"]["Version"];
            return version;
        }
    }

    public class WorkbenchResult
    {
        public WorkbenchStatus Status { get; set; }
        public string Message { get; set; }
    }

    public enum WorkbenchStatus
    {
        NotInstalled,
        ResourceNotFound,
        Running,
        Success,
        Failed
    }

    public class ZipStaticDataSource : IStaticDataSource
    {

        private string _content;
        public ZipStaticDataSource(string content)
        {
            _content = content;
        }

        public Stream GetSource()
        {
            byte[] bytes = Encoding.UTF8.GetBytes(_content);
            return new MemoryStream(bytes);
        }
    }
}
