using BotLib.Extensions;
using BotLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenAI;
using OpenAI.Chat;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Interop;

namespace Bot.ChromeNs
{
    public class MyOpenAI
    {
        public static ChatClient ChatClient { get; set; }

        private static string systemPrompt;
        private static string currentApiKey;
        private static string currentBaseUrl;
        private static string currentModel;
        private static readonly object initSyncObj = new object();

        private static ConcurrentDictionary<string, List<ChatMessage>> buyerChatMessages;

        static MyOpenAI()
        {
            buyerChatMessages = new ConcurrentDictionary<string, List<ChatMessage>>();
            EnsureChatClient();
        }

        private static bool EnsureChatClient()
        {
            string error;
            return EnsureChatClient(out error);
        }

        private static bool EnsureChatClient(out string error)
        {
            error = null;
            var apikey = Params.Robot.GetApiKey();
            var baseUrl = Params.Robot.GetBaseUrl();
            var model = Params.Robot.GetModelName();
            systemPrompt = Params.Robot.GetSystemPrompt();

            if (string.IsNullOrEmpty(apikey) || string.IsNullOrEmpty(model))
            {
                ChatClient = null;
                currentApiKey = apikey;
                currentBaseUrl = baseUrl;
                currentModel = model;
                error = "错误：未配置AI参数，请在设置中配置API密钥和模型名称";
                return false;
            }

            lock (initSyncObj)
            {
                if (ChatClient != null
                    && currentApiKey == apikey
                    && currentBaseUrl == baseUrl
                    && currentModel == model)
                {
                    return true;
                }

                try
                {
                    OpenAIClientOptions options = null;
                    if (!string.IsNullOrEmpty(baseUrl))
                    {
                        Uri endpoint;
                        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out endpoint))
                        {
                            error = "错误：BaseUrl格式不正确，请检查AI接口地址";
                            Log.Error(error);
                            return false;
                        }
                        options = new OpenAIClientOptions
                        {
                            Endpoint = endpoint
                        };
                    }

                    ChatClient = new ChatClient(
                        model: model,
                        credential: new System.ClientModel.ApiKeyCredential(apikey),
                        options: options);
                    currentApiKey = apikey;
                    currentBaseUrl = baseUrl;
                    currentModel = model;
                    return true;
                }
                catch (Exception e)
                {
                    ChatClient = null;
                    error = "错误：AI客户端初始化失败，请检查API Key、模型名称和BaseUrl";
                    Log.Exception(e);
                    Log.Error(error);
                    return false;
                }
            }
        }

        public static string GetAnswer(string seller, string buyer, string question)
        {
            string error;
            if (!EnsureChatClient(out error))
            {
                return error;
            }

            var key = string.Format("{0}#{1}", seller, buyer);
            var messages = buyerChatMessages.xTryGetValue(key);
            if (messages == null || messages.Count < 1)
            {
                messages = new List<ChatMessage>() {
                    ChatMessage.CreateSystemMessage(systemPrompt),
                    ChatMessage.CreateUserMessage(question),
                };
            }
            else
            {
                messages.Add(ChatMessage.CreateUserMessage(question));
            }
            try
            {
                var completion = ChatClient.CompleteChat(messages);
                var completionContent = completion.GetRawResponse().Content.ToString();
                var answer = JObject.Parse(completionContent)["choices"][0]["message"]["content"].ToString();
                messages.Add(ChatMessage.CreateAssistantMessage(answer));
                buyerChatMessages.AddOrUpdate(key, id => messages, (k, v) => messages);
                return answer;
            }
            catch (Exception e)
            {
                Log.Exception(e);
                return "错误：AI调用失败，请检查API Key、模型名称、BaseUrl或网络连接";
            }
        }
    }
}
