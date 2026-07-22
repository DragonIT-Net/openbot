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
    /// Checks for updates, downloads the installer, then starts a silent installation.
    /// </summary>
    public static class UpdateManager
    {
        private const string InstallerArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS";

        /// <summary>Checks silently during application startup.</summary>
        public static Task CheckOnStartupAsync()
        {
            return CheckAndPromptAsync(false);
        }

        /// <summary>Checks for updates when requested from the tray menu.</summary>
        public static Task CheckManuallyAsync()
        {
            return CheckAndPromptAsync(true);
        }

        private static async Task CheckAndPromptAsync(bool isManual)
        {
            UpdateInfo info;
            try
            {
                Log.Info(string.Format("[Update][CheckStart] Manual={0}, CurrentVersion={1}, CurrentVersionNumber={2}", isManual, Params.VersionStr ?? "<empty>", Params.Version));
                info = await UpdateApiClient.GetLatestAsync();
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                if (isManual)
                {
                    MsgBox.ShowErrTip("\u66F4\u65B0\u68C0\u67E5\u5931\u8D25\uFF0C\u8BF7\u7A0D\u540E\u91CD\u8BD5\u3002");
                }
                return;
            }

            int latestVersion;
            if (info == null || !TryParseVersion(info.Version, out latestVersion))
            {
                Log.Error("[Update][InvalidResponse] Version=" + (info == null ? "<null>" : info.Version));
                if (isManual)
                {
                    MsgBox.ShowErrTip("\u66F4\u65B0\u68C0\u67E5\u5931\u8D25\uFF0C\u8BF7\u7A0D\u540E\u91CD\u8BD5\u3002");
                }
                return;
            }

            Log.Info(string.Format("[Update][VersionCompare] Current={0}, LatestRaw={1}, LatestNumber={2}", Params.Version, info.Version ?? "<empty>", latestVersion));
            if (latestVersion <= Params.Version)
            {
                Log.Info(string.Format("[Update][AlreadyLatest] Current={0}, Latest={1}", Params.VersionStr, info.Version));
                if (isManual)
                {
                    MsgBox.ShowTip("当前已是最新版本。", "提示", null, (Action)null);
                }
                return;
            }

            Log.Info(string.Format("[Update][NewVersion] Current={0}, Latest={1}", Params.VersionStr, info.Version));
            var message = string.IsNullOrEmpty(info.ReleaseNotes)
                ? string.Format("\u53d1\u73b0\u65b0\u7248\u672c {0}\uff0c\u662f\u5426\u7acb\u5373\u66f4\u65b0\uff1f", info.Version)
                : string.Format("\u53d1\u73b0\u65b0\u7248\u672c {0}\uff0c\u662f\u5426\u7acb\u5373\u66f4\u65b0\uff1f\r\n\r\n{1}", info.Version, info.ReleaseNotes);

            var confirmed = MsgBox.ShowDialogEx(message, "\u53D1\u73B0\u65B0\u7248\u672C", null, "\u7ACB\u5373\u66F4\u65B0", "\u6682\u4E0D\u66F4\u65B0");
            if (confirmed)
            {
                await DownloadAndInstallAsync(info);
            }
        }

        private static async Task DownloadAndInstallAsync(UpdateInfo info)
        {
            WndLoading.ShowWaiting("\u6B63\u5728\u4E0B\u8F7D\u5E76\u5B89\u88C5\u66F4\u65B0\uFF0C\u7A0B\u5E8F\u7A0D\u540E\u4F1A\u81EA\u52A8\u91CD\u542F\u3002");
            try
            {
                var bytes = await UpdateApiClient.DownloadAsync(info.DownloadUrl);
                var installerPath = Path.Combine(PathEx.TmpPath, "SmartCustomerServiceAssistantUpdate.exe");
                File.WriteAllBytes(installerPath, bytes);
                Log.Info(string.Format("[Update][InstallerSaved] Path={0}, Bytes={1}", installerPath, bytes == null ? 0 : bytes.Length));

                Log.Info("[Update][InstallStarting] Path=" + installerPath);
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
                MsgBox.ShowErrTip("\u66F4\u65B0\u5931\u8D25\uFF0C\u8BF7\u7A0D\u540E\u91CD\u8BD5\u6216\u8054\u7CFB\u7BA1\u7406\u5458\u3002");
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
                // ShareUtil's legacy parser expects a leading "v" (for example v1.0.0).
                // The update API may return either v1.0.0 or 1.0.0, so normalize to that format.
                versionStr = versionStr.Trim();
                if (!versionStr.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                {
                    versionStr = "v" + versionStr;
                }
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
