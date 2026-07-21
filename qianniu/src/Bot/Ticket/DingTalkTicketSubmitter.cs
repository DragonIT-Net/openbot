using BotLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Bot.Ticket
{
    public class DingTalkTicketSubmitter
    {
        public async Task<string> SubmitAsync(string account, TicketFormData form)
        {
            if (string.IsNullOrWhiteSpace(account)) throw new InvalidOperationException("请先在“基础设置”中填写钉钉提交人账号。");
            if (form == null) throw new ArgumentNullException("form");

            var attachments = await UploadAttachmentsAsync(account, form.AttachmentPaths);
            var record = new Dictionary<string, object>();
            record["提交人"] = account;
            record["问题类型"] = GetCategoryText(form.Category);
            record["附件"] = attachments;
            record["提交时间"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            AddIfNotEmpty(record, "电话/ID/订单号", form.CustomerIdentity);
            AddIfNotEmpty(record, "店铺", form.Shop);
            AddIfNotEmpty(record, "问题描述/用户需求", form.Description);
            AddIfNotEmpty(record, "邮箱", form.Email);
            AddIfNotEmpty(record, "是否引导后台自助开票", form.GuideSelfInvoice);
            AddIfNotEmpty(record, "机型", form.DeviceModel);
            AddIfNotEmpty(record, "电话/微信号/远程号", form.ContactChannel);
            AddIfNotEmpty(record, "远程软件", form.RemoteSoftware);
            AddIfNotEmpty(record, "是否可评价", form.CanRate);

            var request = JsonConvert.SerializeObject(new { account = account, records = new[] { record } });
            var response = await TicketApiClient.PostAsync("/dingtalk/records", request);
            Log.Info(string.Format("[钉钉工单提交返回] Account={0}, Response={1}", account, response));
            var result = JObject.Parse(response);
            if (result["success"] != null && !result.Value<bool>("success"))
            {
                throw new InvalidOperationException(result.Value<string>("error") ?? "钉钉工单提交失败。");
            }
            return response;
        }

        private static async Task<List<object>> UploadAttachmentsAsync(string account, List<string> attachmentPaths)
        {
            var attachments = new List<object>();
            if (attachmentPaths == null) return attachments;
            foreach (var attachmentPath in attachmentPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct())
            {
                var file = await ReadAttachmentAsync(attachmentPath);
                var uploadInfoRequest = JsonConvert.SerializeObject(new
                {
                    account = account,
                    size = file.Data.Length,
                    mediaType = file.MediaType,
                    resourceName = file.FileName
                });
                var uploadInfoResponse = await TicketApiClient.PostAsync("/dingtalk/upload-info", uploadInfoRequest);
                var uploadInfo = JObject.Parse(uploadInfoResponse);
                if (uploadInfo["success"] != null && !uploadInfo.Value<bool>("success"))
                {
                    throw new InvalidOperationException(uploadInfo.Value<string>("error") ?? "获取钉钉附件上传信息失败。");
                }
                var data = uploadInfo["data"] as JObject;
                if (data == null || string.IsNullOrEmpty(data.Value<string>("uploadUrl")))
                {
                    throw new InvalidOperationException("钉钉附件上传信息不完整。");
                }
                await TicketApiClient.PutFileAsync(data.Value<string>("uploadUrl"), file.Data, file.MediaType);
                attachments.Add(new
                {
                    filename = file.FileName,
                    size = file.Data.Length,
                    type = file.MediaType,
                    url = data.Value<string>("resourceUrl"),
                    resourceId = data.Value<string>("resourceId")
                });
            }
            return attachments;
        }

        private static async Task<AttachmentFile> ReadAttachmentAsync(string attachmentPath)
        {
            Uri uri;
            byte[] data;
            string fileName;
            if (Uri.TryCreate(attachmentPath, UriKind.Absolute, out uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                data = await TicketApiClient.DownloadAsync(attachmentPath);
                fileName = Path.GetFileName(uri.AbsolutePath);
            }
            else
            {
                if (!File.Exists(attachmentPath)) throw new FileNotFoundException("找不到附件文件", attachmentPath);
                data = File.ReadAllBytes(attachmentPath);
                fileName = Path.GetFileName(attachmentPath);
            }
            if (string.IsNullOrEmpty(fileName)) fileName = "attachment";
            return new AttachmentFile { Data = data, FileName = fileName, MediaType = GetMediaType(fileName) };
        }

        private static void AddIfNotEmpty(Dictionary<string, object> record, string fieldName, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) record[fieldName] = value.Trim();
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

        private static string GetMediaType(string fileName)
        {
            switch (Path.GetExtension(fileName).ToLowerInvariant())
            {
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".png": return "image/png";
                case ".gif": return "image/gif";
                case ".bmp": return "image/bmp";
                case ".pdf": return "application/pdf";
                default: return "application/octet-stream";
            }
        }

        private class AttachmentFile
        {
            public byte[] Data { get; set; }
            public string FileName { get; set; }
            public string MediaType { get; set; }
        }
    }
}
