using Bot.Common;
using Bot.Common.Windows;
using BotLib;
using BotLib.Extensions;
using BotLib.Wpf.Extensions;
using DbEntity;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace Bot.Update
{
    /// <summary>
    /// 检查更新 + 下载安装包 + 拉起静默安装。关闭旧进程/装完重启交给 Inno Setup 的
    /// CloseApplications/RestartApplications 处理，这里不自己写等待/重启逻辑。见 ADR 0005。
    /// </summary>
    public static class UpdateManager
    {
        private const string InstallerArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS";

        /// <summary>启动时调用：静默失败，不打扰用户。</summary>
        public static Task CheckOnStartupAsync()
        {
            return CheckAndPromptAsync(false);
        }

        /// <summary>托盘菜单"检查更新"调用：失败/无更新都要给反馈。</summary>
        public static Task CheckManuallyAsync()
        {
            return CheckAndPromptAsync(true);
        }

        private static async Task CheckAndPromptAsync(bool isManual)
        {
            UpdateInfo info;
            try
            {
                info = await UpdateApiClient.GetLatestAsync();
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                if (isManual)
                {
                    MsgBox.ShowErrTip("检查更新失败，请稍后重试。");
                }
                return;
            }

            int latestVersion;
            if (info == null || !TryParseVersion(info.Version, out latestVersion))
            {
                Log.Error("检查更新接口返回数据异常，Version=" + (info == null ? "<null>" : info.Version));
                if (isManual)
                {
                    MsgBox.ShowErrTip("检查更新失败，请稍后重试。");
                }
                return;
            }

            if (latestVersion <= Params.Version)
            {
                Log.Info(string.Format("[检查更新] 当前已是最新版本。当前={0}, 最新={1}", Params.VersionStr, info.Version));
                if (isManual)
                {
                    MsgBox.ShowTip("当前已是最新版本。");
                }
                return;
            }

            Log.Info(string.Format("[检查更新] 发现新版本。当前={0}, 最新={1}", Params.VersionStr, info.Version));
            var message = string.IsNullOrEmpty(info.ReleaseNotes)
                ? string.Format("发现新版本 {0}，是否立即更新？", info.Version)
                : string.Format("发现新版本 {0}，是否立即更新？\r\n\r\n{1}", info.Version, info.ReleaseNotes);

            var confirmed = MsgBox.ShowDialogEx(message, "发现新版本", null, "立即更新", "暂不更新");
            if (confirmed)
            {
                await DownloadAndInstallAsync(info);
            }
        }

        private static async Task DownloadAndInstallAsync(UpdateInfo info)
        {
            WndLoading.ShowWaiting("正在下载并安装更新，程序稍后会自动重启");
            try
            {
                var bytes = await UpdateApiClient.DownloadAsync(info.DownloadUrl);
                var installerPath = Path.Combine(PathEx.TmpPath, "智能客服助手更新.exe");
                File.WriteAllBytes(installerPath, bytes);

                Log.Info("[检查更新] 安装包下载完成，准备静默安装。Path=" + installerPath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = InstallerArguments,
                    UseShellExecute = true
                });

                DispatcherEx.xInvoke(() =>
                {
                    if (Application.Current != null)
                    {
                        Application.Current.Shutdown();
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                WndLoading.CloseWaiting();
                MsgBox.ShowErrTip("更新失败，请稍后重试或联系管理员。");
            }
        }

        private static bool TryParseVersion(string versionStr, out int version)
        {
            version = 0;
            if (string.IsNullOrEmpty(versionStr))
            {
                return false;
            }
            try
            {
                version = ShareUtil.ConvertStringToVersion(versionStr);
                return true;
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                return false;
            }
        }
    }
}
