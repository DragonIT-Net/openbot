using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using Bot.AssistWindow;
using Bot.Common;
using Bot.Common.Windows;
using BotLib;
using BotLib.Misc;
using BotLib.Wpf.Extensions;
using BotLib.Extensions;
using DbEntity;
using static Bot.Params;
using System.Drawing;
using System.Windows.Media.Imaging;
using System.IO;
using Microsoft.Win32;
using Bot.Common.Trivial;
using Bot.Asset;
using Bot.Automation.ChatDeskNs;
using SuperSocket.Common;
using SuperSocket.SocketEngine.Configuration;

namespace Bot.Options
{
    public partial class CtlRobotOptions : UserControl, IOptions
    {
        private string _seller;
        private string _sellerMain;

        private string _sendImagePath;
        private SynableImageHelper _imageHelper = new SynableImageHelper(TransferFileTypeEnum.RuleAnswerImage);

        public CtlRobotOptions(string seller)
        {
            InitializeComponent();
            InitUI(seller);
        }

        public OptionEnum OptionType
        {
            get
            {
                return OptionEnum.RemindPay;
            }
        }


        public void InitUI(string seller)
        {
            _seller = seller;
            _sellerMain = TbNickHelper.GetMainPart(seller);
            txtQianNiuInstallPath.Text = Params.Robot.GetQianNiuInstallPath();
            txtDingTalkSubmitterAccount.Text = Params.DingTalk.GetSubmitterAccount();

        }

        public void NavHelp()
        {
            throw new NotImplementedException();
        }

        public void RestoreDefault()
        {
            Params.Robot.SetQianNiuInstallPath(string.Empty);
            Params.DingTalk.SetSubmitterAccount(string.Empty);
            txtQianNiuInstallPath.Text = string.Empty;
            txtDingTalkSubmitterAccount.Text = string.Empty;
        }

        public bool Save(string seller)
        {
            var installPath = txtQianNiuInstallPath.Text.Trim();
            if (!string.IsNullOrEmpty(installPath))
            {
                string resourcePath;
                if (!QNInject.TryResolveResourcePath(installPath, out resourcePath))
                {
                    MsgBox.ShowErrDialog(
                        "千牛安装目录无效，请填写千牛程序安装目录。\r\n\r\n需要能找到：Resources\\newWebui\\webui.zip\r\n\r\n当前填写：" + installPath,
                        this);
                    return false;
                }
            }

            Params.Robot.SetQianNiuInstallPath(installPath);
            Params.DingTalk.SetSubmitterAccount(txtDingTalkSubmitterAccount.Text.Trim());
            return true;
        }


        private void btnHelp_Click(object sender, RoutedEventArgs e)
        {

        }


    }
}
