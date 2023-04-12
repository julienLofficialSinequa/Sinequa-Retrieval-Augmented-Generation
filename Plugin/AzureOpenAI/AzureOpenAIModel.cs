///////////////////////////////////////////////////////////
// Plugin OpenAI : file AzureOpenAIModel.cs
//

using System;
using System.Collections.Generic;
using Sinequa.Common;
using Sinequa.Configuration;
using Newtonsoft.Json.Converters;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Azure.AI.OpenAI;
using Azure;

namespace Sinequa.Plugin
{

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ModelType
    {
        [EnumMember(Value = "GPT35Turbo")]
        GPT35Turbo,
        [EnumMember(Value = "GPT4-8K")]
        GPT4_8K,
        [EnumMember(Value = "GPT4-32K")]
        GPT4_32K
    }

    public abstract class AzureOpenAIModel
    {
        public abstract string envVarAPIUrl { get; }
        public abstract string envVarAPIKey { get; }
        public abstract string envVarDeploymentName { get; }
        public abstract ModelType type { get; }
        public abstract int modelSize { get; }

        public string modelDeploymentName { get; internal set; }
        public double temperatureMin { get; internal set; }
        public double temperatureMax { get; internal set; }
        public int generateTokensMin { get; internal set; }
        public int generateTokensMax { get; internal set; }
        public double penaltyMin { get; internal set; }
        public double penaltyMax { get; internal set; }
        public double topPMin { get; internal set; }
        public double topPMax { get; internal set; }
        public int bestOfMin { get; internal set; }
        public int bestOfMax { get; internal set; }

        internal string APIUrl = "";
        internal string APIKey = "";

        public List<SBAChatMessage> messages = new List<SBAChatMessage>();

        public ModelParameters parameters { get; internal set; }

        public ChatCompletionsOptions postBody { get; internal set; }

        public AzureOpenAIModel(ModelParameters modelParams)
        {
            this.temperatureMin = 0;
            this.temperatureMax = 1;
            this.generateTokensMin = 1;
            this.generateTokensMax = 2_000;
            this.penaltyMin = 0;
            this.penaltyMax = 1;
            this.topPMin = 0;
            this.topPMax = 1;
            this.bestOfMin = 1;
            this.bestOfMax = 10;
            this.parameters = modelParams;
        }

        public int CountTokens(string str) => GPTTokenizer.Count(type, str);

        public ChatCompletions QueryAPI(CCPrincipal user, out int HTTPCode, out string errorMessage)
        {
            HTTPCode = 0;
            errorMessage = "";

            OpenAIClient client = new OpenAIClient(new Uri(APIUrl), new AzureKeyCredential(APIKey));

            Response<ChatCompletions> responseWithoutStream = client.GetChatCompletionsAsync(
                modelDeploymentName,
                PostBody(user)
            ).Result;

            if (responseWithoutStream.GetRawResponse().IsError)
            {
                HTTPCode = responseWithoutStream.GetRawResponse().Status;
                errorMessage = responseWithoutStream.GetRawResponse().Content.ToString();
                return null;
            }

            return responseWithoutStream.Value;
        }

        private ChatCompletionsOptions PostBody(CCPrincipal user)
        {
            ChatCompletionsOptions chatCompletionsOptions = new ChatCompletionsOptions()
            {
                Temperature = (float)parameters.temperature,
                MaxTokens = parameters.generateTokens,
                NucleusSamplingFactor = (float)parameters.topP,
                FrequencyPenalty = (float)parameters.frequencyPenalty,
                PresencePenalty = (float)parameters.presencePenalty,
                ChoicesPerPrompt = parameters.bestOf,
                User = user.Id
            };
            messages.ForEach(_ => chatCompletionsOptions.Messages.Add(_.ToChatMessage()));
            postBody = chatCompletionsOptions;
            return chatCompletionsOptions;
        }

        public bool LoadEnvVars(out string errorMessage)
        {
            errorMessage = "";

            APIUrl = CC.Current.EnvVars.Resolve(envVarAPIUrl);
            if (Str.EQ(APIUrl, envVarAPIUrl)) errorMessage = $"Missing environment variable [{envVarAPIUrl}]";
            APIKey = CC.Current.EnvVars.Resolve(envVarAPIKey);
            if (Str.EQ(APIUrl, envVarAPIKey)) errorMessage = $"Missing environment variable [{envVarAPIKey}]";
            modelDeploymentName = CC.Current.EnvVars.Resolve(envVarDeploymentName);
            if (Str.EQ(modelDeploymentName, envVarDeploymentName)) errorMessage = $"Missing environment variable [{envVarDeploymentName}]";

            return string.IsNullOrEmpty(errorMessage);
        }

        public bool CheckInputParams(out string errorMessage)
        {
            errorMessage = "";

            if (parameters.temperature < temperatureMin || parameters.temperature > temperatureMax) errorMessage = $"model.temperature must be between {temperatureMin} and {temperatureMax}";
            if (parameters.generateTokens < generateTokensMin || parameters.generateTokens > generateTokensMax) errorMessage = $"model.generateTokens must be between {generateTokensMin} and {generateTokensMax}";
            if (parameters.frequencyPenalty < penaltyMin || parameters.frequencyPenalty > penaltyMax) errorMessage = $"model.frequencyPenalty must be between {penaltyMin} and {penaltyMax}";
            if (parameters.presencePenalty < penaltyMin || parameters.presencePenalty > penaltyMax) errorMessage = $"model.presencePenalty must be between {penaltyMin} and {penaltyMax}";
            if (parameters.topP < topPMin || parameters.topP > topPMax) errorMessage = $"model.topP must be between {topPMin} and {topPMax}";
            if (parameters.bestOf < bestOfMin || parameters.bestOf > bestOfMax) errorMessage = $"model.bestOf must be between {bestOfMin} and {bestOfMax}";

            return string.IsNullOrEmpty(errorMessage);
        }

        public JsonObject TokensStats(int used, AzureOpenAIQuota quota)
        {
            JsonObject j = new JsonObject();
            j.Set("leftForPrompt", modelSize - used - parameters.generateTokens);
            j.Set("model", modelSize);
            j.Set("generation", parameters.generateTokens);
            j.Set("used", used);
            j.Set("quota", Json.Deserialize(JsonConvert.SerializeObject(quota)));
            return j;
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
    }

    public class AzureOpenAIGPT35Turbo : AzureOpenAIModel
    {
        public AzureOpenAIGPT35Turbo(ModelParameters modelParams) : base(modelParams) 
        { 
            //override model default values here
        }

        public override string envVarAPIUrl => "%%azure-openai-api-url%%";

        public override string envVarAPIKey => "%%azure-openai-api-key%%";

        public override string envVarDeploymentName => "%%azure-openai-gpt-35-deployment-name%%";

        public override ModelType type => ModelType.GPT35Turbo;

        public override int modelSize => 4_097;
    }

    public class AzureOpenAIGPT4_8K : AzureOpenAIModel
    {
        public AzureOpenAIGPT4_8K(ModelParameters modelParams) : base(modelParams)
        {
            //override model default values here
        }

        public override string envVarAPIUrl => "%%azure-openai-api-url%%";

        public override string envVarAPIKey => "%%azure-openai-api-key%%";

        public override string envVarDeploymentName => "%%azure-openai-gpt4-8k-deployment-name%%";

        public override ModelType type => ModelType.GPT4_8K;

        public override int modelSize => 8_192;
    }

    public class AzureOpenAIGPT4_32K : AzureOpenAIModel
    {
        public AzureOpenAIGPT4_32K(ModelParameters modelParams) : base(modelParams)
        {
            //override model default values here
        }

        public override string envVarAPIUrl => "%%azure-openai-api-url%%";

        public override string envVarAPIKey => "%%azure-openai-api-key%%";

        public override string envVarDeploymentName => "%%azure-openai-gpt4-32k-deployment-name%%";

        public override ModelType type => ModelType.GPT4_32K;

        public override int modelSize => 32_768	;
    }

}
