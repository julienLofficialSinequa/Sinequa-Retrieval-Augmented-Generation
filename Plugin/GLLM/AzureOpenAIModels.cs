using System;
using Sinequa.Common;
using Sinequa.Configuration;
using Azure.AI.OpenAI;
using Azure;
using Newtonsoft.Json;
using Sinequa.Search;
using System.Threading.Tasks;
using Sinequa.Search.JsonMethods;
using System.Diagnostics;
using System.Text;
using TiktokenSharp;
using System.Web;
using System.Collections.Generic;
#if NETCOREAPP
using Microsoft.AspNetCore.Http;
#endif


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
        internal ChatCompletionsOptions post;

        public override Json postBody => Json.Deserialize(JsonConvert.SerializeObject(this.post));

        protected AzureOpenAIModel(SearchSession session, ModelParameters modelParams) : base(session, modelParams, TEMP_MIN, TEMP_MAX, GEN_TOKEN_MIN, GEN_TOKEN_MAX, TOP_P_MIN, TOP_P_MAX)
        {
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
            sw.Restart();
            HTTPCode = 0;
            errorMessage = "";

            
            OpenAIClient client = new OpenAIClient(new Uri(APIUrl), new AzureKeyCredential(APIKey));

            Response<ChatCompletions> responseWithoutStream = client.GetChatCompletionsAsync(
                modelDeploymentName,
                PostBody(session.User)
            ).Result;

            if (responseWithoutStream.GetRawResponse().IsError)
            {
                HTTPCode = responseWithoutStream.GetRawResponse().Status;
                errorMessage = responseWithoutStream.GetRawResponse().Content.ToString();
                return null;
            }

            GLLMResponse res = new GLLMResponse(this, sw.ElapsedMilliseconds);
            res.SetResponse(responseWithoutStream.Value);

            return res;
        }

        public override void StreamQueryAPI(JsonMethod method, GLLMQuota quota)
        {
            HttpResponse res = method.Hm.GetContext().Response;

            var q = method.Hm.GetContext().Request;

            OpenAIClient client = new OpenAIClient(new Uri(APIUrl), new AzureKeyCredential(APIKey));

            Response<StreamingChatCompletions> response = client.GetChatCompletionsStreamingAsync(
                modelDeploymentName,
                PostBody(session.User)).Result;

            //set HTTP status code
            res.StatusCode = response.GetRawResponse().Status;
            if (response.GetRawResponse().IsError) return;

#if NETCOREAPP
            //Set HTTP response to event-stream
            res.Headers.Append("Content-Type", "text/event-stream");
            res.Headers.Append("Cache-Control", "no-store");
            res.Headers.Append("Access-Control-Allow-Origin", "*");
            res.Headers.Append("Access-Control-Allow-Credentials", "true");
            res.Body.Flush();

            //token & quota stats
            int tokenCount = CountMessagesTokens();

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            using StreamingChatCompletions streamingChatCompletions = response.Value;

            foreach (StreamingChatChoice choice in streamingChatCompletions.GetChoicesStreaming().ToBlockingEnumerable())
            {
                StringBuilder sb = new StringBuilder();

                foreach (ChatMessage message in choice.GetMessageStreaming().ToBlockingEnumerable())
                {
                    tokenCount += CountTokens(message.Content);
                    sb.Append(message.Content);

                    if (stopwatch.ElapsedMilliseconds >= 500)
                    {
                        FlushMessage(res, EventMessage(sb.ToString(), tokenCount, false));

                        sb.Clear();
                        stopwatch.Restart();
                    }
                }
                FlushMessage(res, EventMessage(sb.ToString(), tokenCount, true));
            }

            //update quota
            //no quota for admins
            if (quota.enabled && method.Session.IsAdmin)
            {
                quota.Update(tokenCount);
            }

#else

            //Set HTTP response to event-stream
            res.AddHeader("Content-Type", "text/event-stream");
            res.AddHeader("Cache-Control", "no-store");
            res.AddHeader("Access-Control-Allow-Origin", "*");
            res.AddHeader("Access-Control-Allow-Credentials", "true");
            res.ClearContent();

            Task _task = getAndDisplayChatAsync(res, method, quota, response);
            _task.Wait();

#endif
            //close stream
            method.Hm.SetResponseEndCalled(true);
        }

        private async Task getAndDisplayChatAsync(HttpResponse res, JsonMethod method, GLLMQuota quota, Response<StreamingChatCompletions> response)
        {
            //token & quota stats
            int tokenCount = CountMessagesTokens();

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();


            using (StreamingChatCompletions streamingChatCompletions = response.Value)
            {
                IAsyncEnumerable<StreamingChatChoice> choices = streamingChatCompletions.GetChoicesStreaming();
                IAsyncEnumerator<StreamingChatChoice> choicesEnumerator = choices.GetAsyncEnumerator();
                try
                {
                    while (await choicesEnumerator.MoveNextAsync())
                    {
                        var choice = choicesEnumerator.Current;

                        StringBuilder sb = new StringBuilder();

                        IAsyncEnumerable<ChatMessage> messages = choice.GetMessageStreaming();
                        IAsyncEnumerator<ChatMessage> messagesEnumerator = messages.GetAsyncEnumerator();

                        try
                        {
                            while (await messagesEnumerator.MoveNextAsync())
                            {
                                var message = messagesEnumerator.Current;

                                tokenCount += CountTokens(message.Content);
                                sb.Append(message.Content);

                                if (stopwatch.ElapsedMilliseconds >= 500)
                                {
                                    FlushMessage(res, EventMessage(sb.ToString(), tokenCount, false));

                                    sb.Clear();
                                    stopwatch.Restart();
                                }
                            }
                        }
                        finally
                        {
                            await messagesEnumerator.DisposeAsync();
                        }

                        FlushMessage(res, EventMessage(sb.ToString(), tokenCount, true));
                    }
                }
                finally
                {
                    await choicesEnumerator.DisposeAsync();
                }

            }

            //update quota
            //no quota for admins
            if (quota.enabled && method.Session.IsAdmin)
            {
                quota.Update(tokenCount);
            }

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
        public static AzureOpenAIGPT35Turbo GetDefaultInstance(SearchSession session)
        {
            return new AzureOpenAIGPT35Turbo(session, null);
        }

        public AzureOpenAIGPT35Turbo(SearchSession session, ModelParameters modelParams) : base(session, modelParams)
        {
            //override model default values here
        }

        public override ModelName name => ModelName.AzureOpenAI_GPT35Turbo;
        
        public override string displayName => "Azure OpenAI - GPT3.5 - 4K Tokens";

        public override int size => 4_097;

        public override bool eventStream => true;

        public override string envVarAPIUrl => "%%azure-openai-api-url%%";

        public override string envVarAPIKey => "%%azure-openai-api-key%%";

        public override string envVarDeploymentName => "%%azure-openai-gpt-35-deployment-name%%";

        public override string envVarPromptProtection => "%%azure-openai-gpt-35-prompt-protection%%";

        private static TikToken tikToken_GPT35 
        { 
            get
            {
                //https://github.com/aiqinxuancai/TiktokenSharp
                //path to p50k_base.tiktoken file
                //default is <sinequa>/<bin|website/bin>/bpe/p50k_base.tiktoken
                //TikToken.PBEFileDirectory = "";
                return TikToken.EncodingForModel("p50k_base");
            }
        }

        public override int Tokenizer(string str, out int HTTPCode, out string errorMessage)
        {
            HTTPCode = 200;
            errorMessage = "";
            return tikToken_GPT35.Encode(str).Count;
        }


    }

    public class AzureOpenAIGPT4_8K : AzureOpenAIModel
    {

        public static AzureOpenAIGPT4_8K GetDefaultInstance(SearchSession session)
        {
            return new AzureOpenAIGPT4_8K(session, null);
        }

        public AzureOpenAIGPT4_8K(SearchSession session, ModelParameters modelParams) : base(session, modelParams)
        {
            //override model default values here
        }

        public override ModelName name => ModelName.AzureOpenAI_GPT4_8K;

        public override string displayName => "Azure OpenAI - GPT4 - 8K Tokens";

        public override int size => 8_192;

        public override bool eventStream => true;

        public override string envVarAPIUrl => "%%azure-openai-api-url%%";

        public override string envVarAPIKey => "%%azure-openai-api-key%%";

        public override string envVarDeploymentName => "%%azure-openai-gpt4-8k-deployment-name%%";

        public override string envVarPromptProtection => "%%azure-openai-gpt4-8k-prompt-protection%%";

        private static TikToken tikToken_GPT4
        {
            get
            {
                //https://github.com/aiqinxuancai/TiktokenSharp
                //path to cl100k_base.tiktoken file
                //default is <sinequa>/<bin|website/bin>/bpe/cl100k_base.tiktoken
                //TikToken.PBEFileDirectory = "";
                return TikToken.EncodingForModel("cl100k_base");
            }
        }

        public override int Tokenizer(string str, out int HTTPCode, out string errorMessage)
        {
            HTTPCode = 200;
            errorMessage = "";
            return tikToken_GPT4.Encode(str).Count;
        }
    }

    public class AzureOpenAIGPT4_32K : AzureOpenAIModel
    {
        public static AzureOpenAIGPT4_32K GetDefaultInstance(SearchSession session)
        {
            return new AzureOpenAIGPT4_32K(session, null);
        }

        public AzureOpenAIGPT4_32K(SearchSession session, ModelParameters modelParams) : base(session, modelParams)
        {
            //override model default values here
        }

        public override ModelName name => ModelName.AzureOpenAI_GPT4_32K;

        public override string displayName => "Azure OpenAI - GPT4 - 32K Tokens";

        public override int size => 32_768;

        public override bool eventStream => true;

        public override string envVarAPIUrl => "%%azure-openai-api-url%%";

        public override string envVarAPIKey => "%%azure-openai-api-key%%";

        public override string envVarDeploymentName => "%%azure-openai-gpt4-32k-deployment-name%%";

        public override string envVarPromptProtection => "%%azure-openai-gpt4-32k-prompt-protection%%";

        private static TikToken tikToken_GPT4
        {
            get
            {
                //https://github.com/aiqinxuancai/TiktokenSharp
                //path to cl100k_base.tiktoken file
                //default is <sinequa>/<bin|website/bin>/bpe/cl100k_base.tiktoken
                //TikToken.PBEFileDirectory = "";
                return TikToken.EncodingForModel("cl100k_base");
            }
        }

        public override int Tokenizer(string str, out int HTTPCode, out string errorMessage)
        {
            HTTPCode = 200;
            errorMessage = "";
            return tikToken_GPT4.Encode(str).Count;
        }
    }

    public class AzureOpenAIGPT35Turbo_16K : AzureOpenAIModel
    {
        public static AzureOpenAIGPT35Turbo_16K GetDefaultInstance(SearchSession session)
        {
            return new AzureOpenAIGPT35Turbo_16K(session, null);
        }

        public AzureOpenAIGPT35Turbo_16K(SearchSession session, ModelParameters modelParams) : base(session, modelParams)
        {
            //override model default values here
        }

        public override ModelName name => ModelName.AzureOpenAI_GPT35Turbo_16K;

        public override string displayName => "Azure OpenAI - GPT3.5 - 16K Tokens";

        public override int size => 16_384;

        public override bool eventStream => true;

        public override string envVarAPIUrl => "%%azure-openai-api-url%%";

        public override string envVarAPIKey => "%%azure-openai-api-key%%";

        public override string envVarDeploymentName => "%%azure-openai-gpt-35-16k-deployment-name%%";

        public override string envVarPromptProtection => "%%azure-openai-gpt-35-16k-prompt-protection%%";

        private static TikToken tikToken_GPT35
        {
            get
            {
                //https://github.com/aiqinxuancai/TiktokenSharp
                //path to p50k_base.tiktoken file
                //default is <sinequa>/<bin|website/bin>/bpe/p50k_base.tiktoken
                //TikToken.PBEFileDirectory = "";
                return TikToken.EncodingForModel("p50k_base");
            }
        }

        public override int Tokenizer(string str, out int HTTPCode, out string errorMessage)
        {
            HTTPCode = 200;
            errorMessage = "";
            return tikToken_GPT35.Encode(str).Count;
        }
    }

}
