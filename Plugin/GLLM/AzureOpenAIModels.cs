using System;
using Sinequa.Common;
using Sinequa.Configuration;
using Azure.AI.OpenAI;
using Azure;
using Newtonsoft.Json;

namespace Sinequa.Plugin
{

    public abstract class AzureOpenAIModel : GLLMModel
    {
        public override ModelProvider provider => ModelProvider.AzureOpenAI;

        [JsonIgnore]
        public abstract string envVarAPIUrl { get; }
        [JsonIgnore]
        public abstract string envVarAPIKey { get; }
        [JsonIgnore]
        public abstract string envVarDeploymentName { get; }
        [JsonIgnore]
        public abstract string envVarPromptProtection { get; }

        public double penaltyMin { get; }
        public double penaltyMax { get; }
        public int bestOfMin { get; }
        public int bestOfMax { get; }

        public const double TEMP_MIN = 0;
        public const double TEMP_MAX = 2;
        public const int GEN_TOKEN_MIN = 1;
        public const int GEN_TOKEN_MAX = 2_000;
        public const double TOP_P_MIN = 0;
        public const double TOP_P_MAX = 1;
        public const double PENALTY_MIN = 0;
        public const double PENALTY_MAX = 1;
        public const int BEST_OF_MIN = 1;
        public const int BEST_OF_MAX = 10;

        internal string APIUrl = "";
        internal string APIKey = "";
        internal string modelDeploymentName = "";
        internal CCPrincipal user;
        internal ChatCompletionsOptions post;

        public override Json postBody => Json.Deserialize(JsonConvert.SerializeObject(this.post));

        protected AzureOpenAIModel(ModelParameters modelParams, CCPrincipal user) : base(modelParams, TEMP_MIN, TEMP_MAX, GEN_TOKEN_MIN, GEN_TOKEN_MAX, TOP_P_MIN, TOP_P_MAX)
        {
            this.user = user;
            this.penaltyMin = PENALTY_MIN;
            this.penaltyMax = PENALTY_MAX;
            this.bestOfMin = BEST_OF_MIN;
            this.bestOfMax = BEST_OF_MAX;
        }

        public override bool CheckInputParams(out string errorMessage)
        {
            errorMessage = "";

            //temperature
            if (parameters.temperature < temperatureMin || parameters.temperature > temperatureMax) errorMessage = $"model.temperature must be between {temperatureMin} and {temperatureMax}";
            //generateTokens
            if (parameters.generateTokens < generateTokensMin || parameters.generateTokens > generateTokensMax) errorMessage = $"model.generateTokens must be between {generateTokensMin} and {generateTokensMax}";
            //frequencyPenalty
            if (parameters.frequencyPenalty < penaltyMin || parameters.frequencyPenalty > penaltyMax) errorMessage = $"model.frequencyPenalty must be between {penaltyMin} and {penaltyMax}";
            //presencePenalty
            if (parameters.presencePenalty < penaltyMin || parameters.presencePenalty > penaltyMax) errorMessage = $"model.presencePenalty must be between {penaltyMin} and {penaltyMax}";
            //topP
            if (parameters.topP < topPMin || parameters.topP > topPMax) errorMessage = $"model.topP must be between {topPMin} and {topPMax}";
            //bestOf
            if (parameters.bestOf < bestOfMin || parameters.bestOf > bestOfMax) errorMessage = $"model.bestOf must be between {bestOfMin} and {bestOfMax}";

            return string.IsNullOrEmpty(errorMessage);
        }

        public override bool LoadEnvVars(out string errorMessage)
        {
            errorMessage = "";

            APIUrl = CC.Current.EnvVars.Resolve(envVarAPIUrl);
            if (Str.EQ(APIUrl, envVarAPIUrl)) errorMessage = $"Missing environment variable [{envVarAPIUrl}]";
            APIKey = CC.Current.EnvVars.Resolve(envVarAPIKey);
            if (Str.EQ(APIUrl, envVarAPIKey)) errorMessage = $"Missing environment variable [{envVarAPIKey}]";
            modelDeploymentName = CC.Current.EnvVars.Resolve(envVarDeploymentName);
            if (Str.EQ(modelDeploymentName, envVarDeploymentName)) errorMessage = $"Missing environment variable [{envVarDeploymentName}]";
            promptProtection = CC.Current.EnvVars.Resolve(envVarPromptProtection);
            if (Str.EQ(promptProtection, envVarPromptProtection)) promptProtection = null;

            return string.IsNullOrEmpty(errorMessage);
        }

        public override GLLMResponse QueryAPI(out int HTTPCode, out string errorMessage)
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

            GLLMResponse res = new GLLMResponse(this.provider);
            res.SetResponse(responseWithoutStream.Value);

            return res;
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
            this.post = chatCompletionsOptions;
            return chatCompletionsOptions;
        }
    }

    public class AzureOpenAIGPT35Turbo : AzureOpenAIModel
    {
        public AzureOpenAIGPT35Turbo(ModelParameters modelParams, CCPrincipal user) : base(modelParams, user)
        {
            //override model default values here
        }

        public override ModelName name => ModelName.GPT35Turbo;

        public override string displayName => "Azure OpenAI - GPT3.5 Turbo";

        public override int size => 4_097;

        public override string envVarAPIUrl => "%%azure-openai-api-url%%";

        public override string envVarAPIKey => "%%azure-openai-api-key%%";

        public override string envVarDeploymentName => "%%azure-openai-gpt-35-deployment-name%%";

        public override string envVarPromptProtection => "%%azure-openai-gpt-35-prompt-protection%%";
    }

    public class AzureOpenAIGPT4_8K : AzureOpenAIModel
    {
        public AzureOpenAIGPT4_8K(ModelParameters modelParams, CCPrincipal user) : base(modelParams, user)
        {
            //override model default values here
        }

        public override ModelName name => ModelName.GPT4_8K;

        public override string displayName => "Azure OpenAI - GPT4 - 8K";

        public override int size => 8_192;

        public override string envVarAPIUrl => "%%azure-openai-api-url%%";

        public override string envVarAPIKey => "%%azure-openai-api-key%%";

        public override string envVarDeploymentName => "%%azure-openai-gpt4-8k-deployment-name%%";

        public override string envVarPromptProtection => "%%azure-openai-gpt4-8k-prompt-protection%%";
    }

    public class AzureOpenAIGPT4_32K : AzureOpenAIModel
    {
        public AzureOpenAIGPT4_32K(ModelParameters modelParams, CCPrincipal user) : base(modelParams, user)
        {
            //override model default values here
        }

        public override ModelName name => ModelName.GPT4_32K;

        public override string displayName => "Azure OpenAI - GPT4 - 32K";

        public override int size => 32_768;

        public override string envVarAPIUrl => "%%azure-openai-api-url%%";

        public override string envVarAPIKey => "%%azure-openai-api-key%%";

        public override string envVarDeploymentName => "%%azure-openai-gpt4-32k-deployment-name%%";

        public override string envVarPromptProtection => "%%azure-openai-gpt4-32k-prompt-protection%%";
    }


}
