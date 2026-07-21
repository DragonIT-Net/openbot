using Bot.Ticket;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Bot.Common.Windows
{
    public partial class WndTicketForm : EtWindow
    {
        private readonly TicketFormData _form;
        private readonly ITicketOptionProvider _optionProvider;
        private readonly DingTalkTicketSubmitter _ticketSubmitter = new DingTalkTicketSubmitter();
        private readonly List<string> _attachmentPaths = new List<string>();

        public WndTicketForm(TicketFormData form, ITicketOptionProvider optionProvider)
        {
            InitializeComponent();
            _form = form ?? new TicketFormData();
            _optionProvider = optionProvider;
            Loaded += WndTicketForm_Loaded;
        }

        private async void WndTicketForm_Loaded(object sender, RoutedEventArgs e)
        {
            txtContext.Text = string.Format("客服：{0}    买家：{1}    类型：{2}", _form.Seller, _form.Buyer, GetCategoryText(_form.Category));
            txtCustomerIdentity.Text = _form.CustomerIdentity;
            txtDescription.Text = (_form.Description ?? string.Empty).Trim();
            AddAttachmentPaths(_form.AttachmentPaths);
            txtEmail.Text = _form.Email;
            txtContactChannel.Text = _form.ContactChannel;
            var visibleFields = TicketFieldDefinitions.GetVisibleFields(_form.Category);
            pnlInvoice.Visibility = visibleFields.Contains("Email") ? Visibility.Visible : Visibility.Collapsed;
            pnlTech.Visibility = visibleFields.Contains("DeviceModel") ? Visibility.Visible : Visibility.Collapsed;
            SetLoading(true, "正在加载下拉选项…");
            try
            {
                await LoadOptionsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("下拉选项加载失败：" + ex.Message, "智能工单", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                SetLoading(false, string.Empty);
            }
        }

        private async Task LoadOptionsAsync()
        {
            var options = await _optionProvider.GetOptionsAsync();
            cmbShop.ItemsSource = options.Shops;
            cmbDeviceModel.ItemsSource = options.DeviceModels;
            cmbRemoteSoftware.ItemsSource = options.RemoteSoftwares;
            cmbGuideSelfInvoice.ItemsSource = options.GuideSelfInvoiceOptions;
            cmbCanRate.ItemsSource = options.CanRateOptions;
            cmbShop.SelectedItem = _form.Shop;
            cmbDeviceModel.SelectedItem = _form.DeviceModel;
            cmbRemoteSoftware.SelectedItem = _form.RemoteSoftware;
            cmbGuideSelfInvoice.SelectedItem = _form.GuideSelfInvoice;
            cmbCanRate.SelectedItem = _form.CanRate;
            if (cmbShop.SelectedIndex < 0 && cmbShop.Items.Count > 0) cmbShop.SelectedIndex = 0;
        }

        private void SetLoading(bool loading, string text)
        {
            pnlLoading.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            if (loading) txtLoading.Text = text;
        }

        private static string GetCategoryText(TicketCategoryEnum category)
        {
            switch (category)
            {
                case TicketCategoryEnum.InvoiceIssue: return "发票类问题";
                case TicketCategoryEnum.TechIssue: return "技术类问题";
                case TicketCategoryEnum.ComplaintIssue: return "客诉类问题";
                default: return "订单类问题";
            }
        }

        private void btnSelectFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Multiselect = true };
            if (dialog.ShowDialog(this) == true)
            {
                AddAttachmentPaths(dialog.FileNames);
            }
        }

        private void AddAttachmentPaths(IEnumerable<string> paths)
        {
            if (paths == null) return;
            foreach (var path in paths)
            {
                var value = (path ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(value) && !_attachmentPaths.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    _attachmentPaths.Add(value);
                }
            }
            RenderAttachments();
        }

        private void RenderAttachments()
        {
            pnlAttachments.Children.Clear();
            foreach (var path in _attachmentPaths)
            {
                var bitmap = IsImage(path) ? LoadBitmap(path) : null;
                var canPreview = bitmap != null;
                var hasAccessibleLocation = HasAccessibleLocation(path);
                var item = new Border
                {
                    Width = canPreview ? 106 : 220,
                    Margin = new Thickness(0, 0, 8, 8),
                    Padding = new Thickness(6),
                    BorderThickness = new Thickness(1),
                    BorderBrush = System.Windows.Media.Brushes.LightGray,
                    CornerRadius = new CornerRadius(3)
                };
                var panel = new StackPanel();
                if (canPreview)
                {
                    var image = new Image { Width = 92, Height = 70, Stretch = System.Windows.Media.Stretch.Uniform, Cursor = Cursors.Hand };
                    image.Source = bitmap;
                    image.ToolTip = "点击预览";
                    image.MouseLeftButtonUp += (sender, e) => ShowImagePreview(path);
                    panel.Children.Add(image);
                }
                panel.Children.Add(new TextBlock
                {
                    Text = canPreview || hasAccessibleLocation ? GetAttachmentName(path) : GetUnavailableAttachmentText(path),
                    Margin = new Thickness(0, 4, 0, 2),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = canPreview ? TextWrapping.NoWrap : TextWrapping.Wrap,
                    MaxWidth = canPreview ? 92 : 190
                });
                var removeButton = new Button { Content = "移除", Tag = path, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(5, 1, 5, 1) };
                removeButton.Click += btnRemoveAttachment_Click;
                panel.Children.Add(removeButton);
                item.Child = panel;
                pnlAttachments.Children.Add(item);
            }
        }

        private void btnRemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var path = button == null ? null : button.Tag as string;
            if (!string.IsNullOrEmpty(path)) _attachmentPaths.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
            RenderAttachments();
        }

        private static bool IsImage(string path)
        {
            var extension = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
            return extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".bmp" || extension == ".gif";
        }

        private static string GetAttachmentName(string path)
        {
            try
            {
                var name = Path.GetFileName(path);
                return string.IsNullOrEmpty(name) ? path : name;
            }
            catch { return path; }
        }

        private static string GetUnavailableAttachmentText(string value)
        {
            return string.Format("{0}\r\n（接口未提供可访问的文件路径或图片地址，无法预览）", GetAttachmentName(value));
        }

        private static bool HasAccessibleLocation(string path)
        {
            Uri uri;
            return File.Exists(path) || (Uri.TryCreate(path, UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
        }

        private static BitmapImage LoadBitmap(string path, int decodePixelWidth = 180)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                if (decodePixelWidth > 0) bitmap.DecodePixelWidth = decodePixelWidth;
                bitmap.UriSource = new Uri(path, UriKind.RelativeOrAbsolute);
                bitmap.EndInit();
                return bitmap;
            }
            catch { return null; }
        }

        private void ShowImagePreview(string path)
        {
            var bitmap = LoadBitmap(path, 0);
            if (bitmap == null) return;
            var preview = new Window
            {
                Title = GetAttachmentName(path),
                Owner = this,
                Width = 820,
                Height = 620,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new Image { Source = bitmap, Stretch = System.Windows.Media.Stretch.Uniform, Margin = new Thickness(12) }
            };
            preview.ShowDialog();
        }

        private async void btnConfirm_Click(object sender, RoutedEventArgs e)
        {
            UpdateFormData();
            SetLoading(true, "正在提交工单…");
            try
            {
                await _ticketSubmitter.SubmitAsync(Bot.Params.DingTalk.GetSubmitterAccount(), _form);
                MessageBox.Show("工单已提交到钉钉 AI 表格。", "智能工单", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("工单提交失败：" + ex.Message, "智能工单", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetLoading(false, string.Empty);
            }
        }

        private void UpdateFormData()
        {
            _form.CustomerIdentity = txtCustomerIdentity.Text;
            _form.Shop = GetComboBoxText(cmbShop);
            _form.Description = txtDescription.Text;
            _form.AttachmentPaths = _attachmentPaths.ToList();
            _form.Email = txtEmail.Text;
            _form.GuideSelfInvoice = GetComboBoxText(cmbGuideSelfInvoice);
            _form.DeviceModel = GetComboBoxText(cmbDeviceModel);
            _form.ContactChannel = txtContactChannel.Text;
            _form.RemoteSoftware = GetComboBoxText(cmbRemoteSoftware);
            _form.CanRate = GetComboBoxText(cmbCanRate);
        }

        private static string GetComboBoxText(ComboBox comboBox)
        {
            var item = comboBox.SelectedItem as ComboBoxItem;
            if (item != null) return item.Content == null ? string.Empty : item.Content.ToString();
            return comboBox.SelectedItem == null ? string.Empty : comboBox.SelectedItem.ToString();
        }
        private void btnCancel_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
