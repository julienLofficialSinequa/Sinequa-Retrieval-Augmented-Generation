///////////////////////////////////////////////////////////
// Plugin OpenAI : file AzureOpenAI.cs
//

using System;
using System.Collections.Generic;
using Sinequa.Common;
using Sinequa.Configuration;
using Sinequa.Plugins;
using Sinequa.Search.JsonMethods;
using Newtonsoft.Json;
using System.Linq;
using Azure.AI.OpenAI;

namespace Sinequa.Plugin
{

    public class AzureOpenAI : JsonMethodPlugin
    {
        public const string ENV_VAR_PROMPT_PROTECTION = "%%azure-openai-prompt-protection%%"; 
        public string promptProtection;

        public const string ENV_VAR_USER_QUOTA = "%%azure-openai-user-quota-tokens%%";
        public int quotaPeriodTokens;

        public const string ENV_VAR_REST_HOURS = "%%azure-openai-user-quota-reset-hours%%";
        public int quotaResetHours;

        public override JsonMethodAuthLevel GetRequiredAuthLevel()
        {
            return JsonMethodAuthLevel.User;
        }

        public override void OnPluginMethod()
        {
            if (!LoadEnvVars(out string errorMessage))
            {
                this.SetError(500, errorMessage);
                return;
            }

            string input = Json.Serialize(this.Method.JsonRequest);
            InputParameters inputPrams = JsonConvert.DeserializeObject<InputParameters>(input);

            switch (inputPrams.action)
            {
                case InputParameters.Action.ListModels:
                    ListModels();
                    break;
                case InputParameters.Action.Chat:
                    inputPrams = JsonConvert.DeserializeObject<InputParametersChat>(input);
                    Chat((InputParametersChat)inputPrams);
                    break;
                case InputParameters.Action.TokenCount:
                    inputPrams = JsonConvert.DeserializeObject<InputParametersTokensCount>(input);
                    TokensCount((InputParametersTokensCount)inputPrams);
                    break;
                default:
                    this.SetError(500, "Action no implemented");
                    return;
            }
        }

        private void Chat(InputParametersChat inputParamsChat)
        {
            AzureOpenAIQuota quota = new AzureOpenAIQuota(quotaPeriodTokens, quotaResetHours, this.Method.Session.UserSettings);

            if (quota.enabled && !quota.Allowed())
            {
                this.SetError(429, $"Max quota reached [{quota.tokenCount}], retry after [{quota.nextReset}] UTC");
                return;
            }

            AzureOpenAIModel model;
            string errorMessage;

            switch (inputParamsChat.model.name)
            {
                case ModelType.GPT35Turbo:
                    model = new AzureOpenAIGPT35Turbo(inputParamsChat.model);
                    break;
                case ModelType.GPT4_8K:
                    model = new AzureOpenAIGPT4_8K(inputParamsChat.model);
                    break;
                case ModelType.GPT4_32K:
                    model = new AzureOpenAIGPT4_32K(inputParamsChat.model);
                    break;
                default:
                    this.SetError("invalid model");
                    return;
            }

            if (!model.LoadEnvVars(out errorMessage))
            {
                this.SetError(500, errorMessage);
                return;
            }
            if (!model.CheckInputParams(out errorMessage))
            {
                this.SetError(500, errorMessage);
                return;
            }

            model.messages = inputParamsChat.messagesHistory;

            if (inputParamsChat.promptProtection)
            {
                model.AddMessageBeforeLast(ChatRole.User, promptProtection);
            }

            ChatCompletions res = model.QueryAPI(this.Method.Session.User, out int HTTPCode , out errorMessage);
            if (res == null)
            {
                this.SetError(500, $"[{HTTPCode}] {errorMessage}");
                return;
            }

            if (inputParamsChat.debug)
            {
                this.Method.JsonResponse.Set($"post_{inputParamsChat.model.name}", Json.Deserialize(JsonConvert.SerializeObject(model.postBody)));
                this.Method.JsonResponse.Set($"response_{inputParamsChat.model.name}", Json.Deserialize(JsonConvert.SerializeObject(res)));
            }

            ChatChoice choice = res.Choices.First();

            model.AddMessage(choice.Message.Role, choice.Message.Content, true);

            this.JsonResponse.Set("messagesHistory", Json.Deserialize(JsonConvert.SerializeObject(model.messages)));

            int modelTokenGenerationConsumption = res.Usage.TotalTokens;

            //no quota for admins
            if (quota.enabled && !this.Method.Session.IsAdmin)
            {
                quota.Update(modelTokenGenerationConsumption);
            }

            this.Method.JsonResponse.Set("tokens", model.TokensStats(modelTokenGenerationConsumption, quota));

        }

        private void TokensCount(InputParametersTokensCount inputParamsTokensCount)
        {
            List<int> count = GPTTokenizer.Count(inputParamsTokensCount.model, inputParamsTokensCount.text);
            this.Method.JsonResponse.Set("tokens", count);
        }

        private bool LoadEnvVars(out string errorMessage)
        {
            errorMessage = "";
            string s;

            promptProtection = CC.Current.EnvVars.Resolve(ENV_VAR_PROMPT_PROTECTION);
            if (Str.EQ(promptProtection, ENV_VAR_PROMPT_PROTECTION))
            {
                errorMessage = $"Missing environment variable [{ENV_VAR_PROMPT_PROTECTION}]";
                return false;
            }

            s = CC.Current.EnvVars.Resolve(ENV_VAR_USER_QUOTA);
            if (Str.EQ(s, ENV_VAR_USER_QUOTA))
            {
                errorMessage = $"Missing environment variable [{ENV_VAR_USER_QUOTA}]";
                return false;
            }
            if (!int.TryParse(s, out quotaPeriodTokens))
            {
                errorMessage = $"Invalid environment variable [{ENV_VAR_USER_QUOTA}]";
                return false;
            }

            s = CC.Current.EnvVars.Resolve(ENV_VAR_REST_HOURS);
            if (Str.EQ(s, ENV_VAR_REST_HOURS))
            {
                errorMessage = $"Missing environment variable [{ENV_VAR_REST_HOURS}]";
                return false;
            }
            if (!int.TryParse(s, out quotaResetHours))
            {
                errorMessage = $"Invalid environment variable [{ENV_VAR_REST_HOURS}]";
                return false;
            }

            return String.IsNullOrEmpty(errorMessage);
        }

        private void ListModels()
        {
            AzureOpenAIModel model;
            string errorMessage;

            List<ModelType> lModels = new List<ModelType>();

            model = new AzureOpenAIGPT35Turbo(new ModelParameters() { name = ModelType.GPT35Turbo });
            if (model.LoadEnvVars(out errorMessage)) lModels.Add(model.type);

            model = new AzureOpenAIGPT4_8K(new ModelParameters() { name = ModelType.GPT4_8K });
            if (model.LoadEnvVars(out errorMessage)) lModels.Add(model.type);

            model = new AzureOpenAIGPT4_32K(new ModelParameters() { name = ModelType.GPT4_32K });
            if (model.LoadEnvVars(out errorMessage)) lModels.Add(model.type);

            this.JsonResponse.Set("models", Json.Deserialize(JsonConvert.SerializeObject(lModels)));
        }
    }
}
