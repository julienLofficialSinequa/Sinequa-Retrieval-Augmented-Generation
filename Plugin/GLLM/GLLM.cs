using System;
using System.Collections.Generic;
using Sinequa.Common;
using Sinequa.Configuration;
using Sinequa.Plugins;
using Sinequa.Search.JsonMethods;
using Newtonsoft.Json;

namespace Sinequa.Plugin
{

    public class GLLM : JsonMethodPlugin
    {
        public const string ENV_VAR_USER_QUOTA = "%%gllm-user-quota-tokens%%";
        public int quotaPeriodTokens;

        public const string ENV_VAR_REST_HOURS = "%%gllm-user-quota-reset-hours%%";
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
            GLLMQuota quota = new GLLMQuota(quotaPeriodTokens, quotaResetHours, this.Method.Session.UserSettings);

            if (quota.enabled && !quota.Allowed())
            {
                this.SetError(429, $"Max quota reached [{quota.tokenCount}], retry after [{quota.nextReset}] UTC");
                return;
            }

            GLLMModel model;
            string errorMessage;

            switch (inputParamsChat.model.name)
            {
                case ModelName.GPT35Turbo:
                    model = new AzureOpenAIGPT35Turbo(inputParamsChat.model, this.Method.Session.User);
                    break;
                case ModelName.GPT4_8K:
                    model = new AzureOpenAIGPT4_8K(inputParamsChat.model, this.Method.Session.User);
                    break;
                case ModelName.GPT4_32K:
                    model = new AzureOpenAIGPT4_32K(inputParamsChat.model, this.Method.Session.User);
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

            if (inputParamsChat.promptProtection) model.AddPromptProtection();

            GLLMResponse res = model.QueryAPI(out int HTTPCode , out errorMessage);
            if (res == null)
            {
                this.SetError(500, $"[{HTTPCode}] {errorMessage}");
                return;
            }

            if (inputParamsChat.debug)
            {
                this.Method.JsonResponse.Set($"post_{inputParamsChat.model.name}", model.postBody);
                this.Method.JsonResponse.Set($"response_{inputParamsChat.model.name}", res.JsonResponse());
            }

            SBAChatMessage chatMsg = res.GetMessage();

            model.AddMessage(chatMsg);

            this.JsonResponse.Set("messagesHistory", model.JsonMessages);

            //no quota for admins
            if (quota.enabled && !this.Method.Session.IsAdmin)
            {
                quota.Update(res.UsageTotalTokens);
            }

            this.Method.JsonResponse.Set("tokens", model.TokensStats(res.UsageTotalTokens, quota));

        }

        private void TokensCount(InputParametersTokensCount inputParamsTokensCount)
        {
            List<int> count = GLLMTokenizer.Count(inputParamsTokensCount.model, inputParamsTokensCount.text);
            this.Method.JsonResponse.Set("tokens", count);
        }

        private bool LoadEnvVars(out string errorMessage)
        {
            errorMessage = "";
            string s;

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
            GLLMModel model;
            string errorMessage;

            List<GLLMModel> lModels = new List<GLLMModel>();

            model = new AzureOpenAIGPT35Turbo(new ModelParameters() { name = ModelName.GPT35Turbo }, null);
            if (model.LoadEnvVars(out errorMessage)) lModels.Add(model);

            model = new AzureOpenAIGPT4_8K(new ModelParameters() { name = ModelName.GPT4_8K }, null);
            if (model.LoadEnvVars(out errorMessage)) lModels.Add(model);

            model = new AzureOpenAIGPT4_32K(new ModelParameters() { name = ModelName.GPT4_32K }, null);
            if (model.LoadEnvVars(out errorMessage)) lModels.Add(model);

            this.JsonResponse.Set("models", Json.Deserialize(JsonConvert.SerializeObject(lModels)));
        }
    }
}
