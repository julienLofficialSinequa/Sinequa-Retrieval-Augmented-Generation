using System;
using System.Collections.Generic;
using Sinequa.Common;
using Sinequa.Configuration;
using Newtonsoft.Json.Converters;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Azure.AI.OpenAI;
using System.Linq;

namespace Sinequa.Plugin
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ModelProvider
    {
        [EnumMember(Value = "OpenAI")]
        AzureOpenAI
    }


    [JsonConverter(typeof(StringEnumConverter))]
    public enum ModelName
    {
        [EnumMember(Value = "GPT35Turbo")]
        GPT35Turbo,
        [EnumMember(Value = "GPT4-8K")]
        GPT4_8K,
        [EnumMember(Value = "GPT4-32K")]
        GPT4_32K
    }


    public abstract class GLLMModel
    {
        public abstract ModelProvider provider { get; }
        public abstract ModelName name { get; }
        public abstract string displayName { get; }
        public abstract int size { get; }

        [JsonIgnore]
        public abstract Json postBody { get; }

        public double temperatureMin { get; }
        public double temperatureMax { get; }
        public int generateTokensMin { get; }
        public int generateTokensMax { get; }
        public double topPMin { get; }
        public double topPMax { get; }

        [JsonIgnore]
        public ModelParameters parameters { get; }

        [JsonIgnore]
        public List<SBAChatMessage> messages = new List<SBAChatMessage>();

        [JsonIgnore]
        public Json JsonMessages
        {
            get => Json.Deserialize(JsonConvert.SerializeObject(messages));
        }
        [JsonIgnore]
        public string promptProtection = String.Empty;

        public GLLMModel(ModelParameters modelParams, double temperatureMin, double temperatureMax, int generateTokensMin, int generateTokensMax, double topPMin, double topPMax)
        {
            this.temperatureMin = temperatureMin;
            this.temperatureMax = temperatureMax;
            this.generateTokensMin = generateTokensMin;
            this.generateTokensMax = generateTokensMax;
            this.topPMin = topPMin;
            this.topPMax = topPMax;
            this.parameters = modelParams;
        }

        public abstract GLLMResponse QueryAPI(out int HTTPCode, out string errorMessage);

        public abstract bool LoadEnvVars(out string errorMessage);

        public abstract bool CheckInputParams(out string errorMessage);

        public int CountTokens(string str) => GLLMTokenizer.Count(name, str);

        public JsonObject TokensStats(int used, GLLMQuota quota)
        {
            JsonObject j = new JsonObject();
            j.Set("leftForPrompt", size - used - parameters.generateTokens);
            j.Set("model", size);
            j.Set("generation", parameters.generateTokens);
            j.Set("used", used);
            j.Set("quota", Json.Deserialize(JsonConvert.SerializeObject(quota)));
            return j;
        }

        public void AddPromptProtection()
        {
            if(!string.IsNullOrEmpty(promptProtection)) AddMessageBeforeLast(ChatRole.User, promptProtection);
        }

        public void AddMessageBeforeLast(ChatRole role, string content)
        {
            if (messages.Count <= 2) return;
            messages.Insert(messages.Count - 2, new SBAChatMessage(role, content, false));
        }

        public void AddMessage(ChatRole role, string content, bool display)
        {
            messages.Add(new SBAChatMessage(role, content, display));
        }

        public void AddMessage(SBAChatMessage SBAChatMessage)
        {
            messages.Add(SBAChatMessage);
        }

    }

    public class GLLMResponse
    {
        public ModelProvider provider { get; }

        private ChatCompletions AzureOpenAIResponse;

        public int UsageTotalTokens => AzureOpenAIResponse.Usage.TotalTokens;

        public GLLMResponse(ModelProvider provider) 
        { 
            this.provider = provider;
        }

        public void SetResponse(object response)
        {
            switch (this.provider)
            {
                case ModelProvider.AzureOpenAI:
                    AzureOpenAIResponse = (ChatCompletions)response; 
                    break;
                default: throw new NotImplementedException();
            }
        }

        public Json JsonResponse()
        {
            switch (this.provider)
            {
                case ModelProvider.AzureOpenAI:
                    return Json.Deserialize(JsonConvert.SerializeObject(AzureOpenAIResponse));
                default: throw new NotImplementedException();
            }
        }

        public SBAChatMessage GetMessage()
        {
            SBAChatMessage msg;

            switch (this.provider)
            {
                case ModelProvider.AzureOpenAI:
                    ChatChoice choice = AzureOpenAIResponse.Choices.First();
                    msg = new SBAChatMessage(choice.Message.Role, choice.Message.Content, true);
                    break;
                default: throw new NotImplementedException();
            }
            return msg;
        }
    }

}
