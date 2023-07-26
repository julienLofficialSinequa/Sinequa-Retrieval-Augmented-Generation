using System;
using System.Collections.Generic;
using Sinequa.Common;
using Sinequa.Configuration;
using Newtonsoft.Json.Converters;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Azure.AI.OpenAI;
using System.Linq;
using Sinequa.Search;
using Sinequa.Web;
using Sinequa.Search.JsonMethods;
using System.Diagnostics;
using System.Web;
#if NETCOREAPP
using Microsoft.AspNetCore.Http;
#endif

namespace Sinequa.Plugin
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ModelProvider
    {
        [EnumMember(Value = "OpenAI")]
        AzureOpenAI,
        [EnumMember(Value = "Google")]
        Google,
        [EnumMember(Value = "Cohere")]
        Cohere
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ModelName
    {
        [EnumMember(Value = "GPT35Turbo")]
        AzureOpenAI_GPT35Turbo,
        [EnumMember(Value = "GPT35Turbo-16K")]
        AzureOpenAI_GPT35Turbo_16K,
        [EnumMember(Value = "GPT4-8K")]
        AzureOpenAI_GPT4_8K,
        [EnumMember(Value = "GPT4-32K")]
        AzureOpenAI_GPT4_32K,
        [EnumMember(Value = "Chat-Bison-001")]
        GoogleVertex_Chat_Bison_001,
        [EnumMember(Value = "command-nightly")]
        Cohere_Command_XL_Beta
    }

    public abstract class GLLMModel
    {
        public abstract ModelProvider provider { get; }
        public abstract ModelName name { get; }

        public abstract string displayName { get; }
        public abstract int size { get; }
        public abstract bool eventStream { get; }

        [JsonIgnore]
        public abstract Json postBody { get; }

        public double temperatureMin { get; }
        public double temperatureMax { get; }
        public int generateTokensMin { get; }
        public int generateTokensMax { get; }
        public double topPMin { get; }
        public double topPMax { get; }

        internal Stopwatch sw = new Stopwatch();

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

        [JsonIgnore]
        internal SearchSession session;

        public GLLMModel(SearchSession session, ModelParameters modelParams, double temperatureMin, double temperatureMax, int generateTokensMin, int generateTokensMax, double topPMin, double topPMax)
        {
            this.session = session;
            this.temperatureMin = temperatureMin;
            this.temperatureMax = temperatureMax;
            this.generateTokensMin = generateTokensMin;
            this.generateTokensMax = generateTokensMax;
            this.topPMin = topPMin;
            this.topPMax = topPMax;
            this.parameters = modelParams;
        }

        public abstract GLLMResponse QueryAPI(out int HTTPCode, out string errorMessage);

        public abstract void StreamQueryAPI(JsonMethod method, GLLMQuota quota);

        public abstract bool LoadEnvVars(out string errorMessage);

        public abstract bool CheckInputParams(out string errorMessage);

        public abstract int Tokenizer(string str, out int HTTPCode, out string errorMessage);
        public int CountTokens(string str)
        {
            if (String.IsNullOrEmpty(str)) return 0;
            return Tokenizer(str, out int HTTPCode, out string errorMessage);
        }
        public int CountMessagesTokens() => messages.Sum(_ => CountTokens(_.content));

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

        public void AddMessage(ChatRole role, string content, bool display) => messages.Add(new SBAChatMessage(role, content, display));

        public void AddMessage(SBAChatMessage SBAChatMessage) => messages.Add(SBAChatMessage);

        internal void FlushMessage(HttpResponse response, Json json)
        {
#if NETCOREAPP
            response.WriteAsync($"data: {Json.Serialize(json, false)}\n\n");
            response.Body.Flush();
#else
            response.Write($"data: {Json.Serialize(json, false)}\n\n");
            response.Flush();
#endif
        }

        internal Json EventMessage(string content, int tokens, bool stop = false)
        {
            JsonObject j = new JsonObject();
            j.Set("content", content);
            j.Set("tokens", tokens);
            j.Set("stop", stop);
            return j;
        }
    }

    public class GLLMResponse
    {
        private GLLMModel _model;
        private object _response;
        public long generationAPITime { get; }

        public GLLMResponse(GLLMModel model, long generationAPITime)
        {
            this._model = model;
            this.generationAPITime = generationAPITime;
        }

        public void SetResponse(object response)
        {
            _response = response;
        }

        public int UsageTotalTokens
        {
            get
            {
                switch (_model.name)
                {
                    case ModelName.AzureOpenAI_GPT35Turbo:
                    case ModelName.AzureOpenAI_GPT4_8K:
                    case ModelName.AzureOpenAI_GPT4_32K:
                        return (_response as ChatCompletions).Usage.TotalTokens;
                    case ModelName.GoogleVertex_Chat_Bison_001:
                        return (_response as VertexAIBisonResponse).predictions.Sum(_ => _.candidates.Sum(__ => _model.CountTokens(__.content)));
                    case ModelName.Cohere_Command_XL_Beta:
                        var res = _response as CohereCommandResponse;
                        return res.generations.Sum(_ => _model.CountTokens(_.text)) + _model.CountTokens(res.prompt);
                    default:
                        return 0;
                }
            }
        }

        public Json JsonResponse()
        {
            return Json.Deserialize(JsonConvert.SerializeObject(_response));
        }

        public SBAChatMessage GetMessage()
        {
            switch (_model.name)
            {
                case ModelName.AzureOpenAI_GPT35Turbo:
                case ModelName.AzureOpenAI_GPT4_8K:
                case ModelName.AzureOpenAI_GPT4_32K:
                    ChatChoice choice = (_response as ChatCompletions).Choices.First();
                    return new SBAChatMessage(choice.Message.Role, choice.Message.Content, true);
                case ModelName.GoogleVertex_Chat_Bison_001:
                    VertexAIChatMessage condidate = (_response as VertexAIBisonResponse).predictions.First().candidates.First();
                    return new SBAChatMessage(condidate.role, condidate.content, true);
                case ModelName.Cohere_Command_XL_Beta:
                    CohereCommandGeneration generation = (_response as CohereCommandResponse).generations.First();
                    return new SBAChatMessage(ChatRole.Assistant, generation.text, true);
                default: throw new NotImplementedException();
            }
        }
    }

}
