using System;
using System.Collections.Generic;
using Sinequa.Common;
using Sinequa.Configuration;
using Sinequa.Plugins;
using Sinequa.Search.JsonMethods;
using Newtonsoft.Json;
using Sinequa.Configuration.SBA;
using Sinequa.Search;
using System.Linq;
using Azure.AI.OpenAI;

namespace Sinequa.Plugin
{

    public class GLLM : JsonMethodPlugin
    {
        public const string ENV_VAR_USER_QUOTA = "%%gllm-user-quota-tokens%%";
        public int quotaPeriodTokens;

        public const string ENV_VAR_REST_HOURS = "%%gllm-user-quota-reset-hours%%";
        public int quotaResetHours;

        private ICCApp app = null;
        private CCQuery ccQuery = null;
        private GLLMResponse modelResponse = null;

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
                case InputParameters.Action.Quota:
                    Quota();
                    break;
                case InputParameters.Action.Context:
                    inputPrams = JsonConvert.DeserializeObject<InputParametersContext>(input);
                    Context((InputParametersContext)inputPrams);
                    break;
                case InputParameters.Action.Answer:
                    inputPrams = JsonConvert.DeserializeObject<InputParametersAnswer>(input);
                    Answer((InputParametersAnswer)inputPrams);
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
                case ModelName.AzureOpenAI_GPT35Turbo:
                    model = new AzureOpenAIGPT35Turbo(this.Method.Session, inputParamsChat.model);
                    break;
                case ModelName.AzureOpenAI_GPT4_8K:
                    model = new AzureOpenAIGPT4_8K(this.Method.Session, inputParamsChat.model);
                    break;
                case ModelName.AzureOpenAI_GPT4_32K:
                    model = new AzureOpenAIGPT4_32K(this.Method.Session, inputParamsChat.model);
                    break;
                case ModelName.GoogleVertex_Chat_Bison_001:
                    model = new GoogleVertexAIBison_001(this.Method.Session, inputParamsChat.model);
                    break;
                case ModelName.Cohere_Command_XL_Beta:
                    model = new CohereGeneration(this.Method.Session, inputParamsChat.model);
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

            //STREAMING
            if (model.eventStream && inputParamsChat.stream)
            {
                model.StreamQueryAPI(this.Method, quota);
            }
            else
            {
                GLLMResponse res = model.QueryAPI(out int HTTPCode, out errorMessage);
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
                modelResponse = res;
                model.AddMessage(chatMsg);

                this.JsonResponse.Set("messagesHistory", model.JsonMessages);

                //no quota for admins
                if (quota.enabled && !this.Method.Session.IsAdmin)
                {
                    quota.Update(res.UsageTotalTokens);
                }

                this.Method.JsonResponse.Set("tokens", model.TokensStats(res.UsageTotalTokens, quota));
            }

        }

        private void TokensCount(InputParametersTokensCount inputParamsTokensCount)
        {
            GLLMModel model;

            switch (inputParamsTokensCount.model)
            {
                case ModelName.AzureOpenAI_GPT35Turbo:
                    model = AzureOpenAIGPT35Turbo.GetDefaultInstance(this.Method.Session);
                    break;
                case ModelName.AzureOpenAI_GPT4_8K:
                    model = AzureOpenAIGPT4_8K.GetDefaultInstance(this.Method.Session);
                    break;
                case ModelName.AzureOpenAI_GPT4_32K:
                    model = AzureOpenAIGPT4_32K.GetDefaultInstance(this.Method.Session);
                    break;
                case ModelName.GoogleVertex_Chat_Bison_001:
                    model = GoogleVertexAIBison_001.GetDefaultInstance(this.Method.Session);
                    break;
                case ModelName.Cohere_Command_XL_Beta:
                    model = CohereGeneration.GetDefaultInstance(this.Method.Session);
                    break;
                default:
                    this.SetError("invalid model");
                    return;
            }

            model.LoadEnvVars(out string errorMessage);
            if (!string.IsNullOrEmpty(errorMessage))
            {
                this.SetError(500, errorMessage);
                return;
            }

            List<int> lTokenCount = inputParamsTokensCount.text.Select(_ => model.CountTokens(_)).ToList();

            this.Method.JsonResponse.Set("tokens", lTokenCount);
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

            model = AzureOpenAIGPT35Turbo.GetDefaultInstance(this.Method.Session);
            if (model.LoadEnvVars(out errorMessage)) lModels.Add(model);

            model = AzureOpenAIGPT4_8K.GetDefaultInstance(this.Method.Session);
            if (model.LoadEnvVars(out errorMessage)) lModels.Add(model);

            model = AzureOpenAIGPT4_32K.GetDefaultInstance(this.Method.Session);
            if (model.LoadEnvVars(out errorMessage)) lModels.Add(model);

            model = GoogleVertexAIBison_001.GetDefaultInstance(this.Method.Session);
            if (model.LoadEnvVars(out errorMessage)) lModels.Add(model);

            model = CohereGeneration.GetDefaultInstance(this.Method.Session);
            if (model.LoadEnvVars(out errorMessage)) lModels.Add(model);

            this.JsonResponse.Set("models", Json.Deserialize(JsonConvert.SerializeObject(lModels)));
        }

        private void Quota()
        {
            GLLMQuota quota = new GLLMQuota(quotaPeriodTokens, quotaResetHours, this.Method.Session.UserSettings);
            if (quota.enabled) quota.Allowed();

            JsonObject j = new JsonObject();
            this.Method.JsonResponse.Set("quota", Json.Deserialize(JsonConvert.SerializeObject(quota)));
        }

        private Json GetQueryResponse(InputParametersAppQuery inputQuery, out long queryExecutionTime)
        {
            queryExecutionTime = 0;

            if (!CheckAppQuery(inputQuery)) return null;

            Json jQuery = this.Method.JsonRequest.Get("query");

            GLLMSearch search = new GLLMSearch(this.app, this.ccQuery, this.Method.Session, jQuery);

            Json jQueryResponse = search.ExecuteQuery(out queryExecutionTime);
            if (jQueryResponse == null)
            {
                this.SetError(500, $"Cannot execute query [{ccQuery.Name}]");
                return null;
            }
            if (jQueryResponse.EltExist("ErrorCode"))
            {
                this.SetError(500, jQueryResponse.ValueStr("ErrorMessage"));
                return null;
            }
            return jQueryResponse;
        }

        private void Context(InputParametersContext inputParamsContext)
        {
            Json jQueryResponse = GetQueryResponse(inputParamsContext, out long queryExecutionTime);
            if (jQueryResponse == null) return;

            var context = GLLMContext.FromSearch(
                jQueryResponse,
                inputParamsContext.contextOptions,
                this.Method.Session,
                this.app.Name,
                this.ccQuery.Name
            );

            var strContext = context.GetDocumentsContext();

            this.JsonResponse.Set("context", strContext);
            if (inputParamsContext.debug) this.JsonResponse.Set("GLLMContext", Json.Deserialize(JsonConvert.SerializeObject(context)));
            if (inputParamsContext.debug) this.JsonResponse.Set("res", jQueryResponse);
        }

        

        private void Answer(InputParametersAnswer inputParamsAnswer)
        {
            Json jQueryResponse = GetQueryResponse(inputParamsAnswer, out long queryExecutionTime);
            if (jQueryResponse == null) return;


            var context = GLLMContext.FromSearch(
                jQueryResponse,
                inputParamsAnswer.contextOptions,
                this.Method.Session,
                this.app.Name,
                this.ccQuery.Name
            );

            var strContext = context.GetPromptContext(inputParamsAnswer.prompt);

            List<SBAChatMessage> lMessages = new List<SBAChatMessage>();
            if(!String.IsNullOrEmpty(inputParamsAnswer.prompt.systemPrompt)){
                lMessages.Add(new SBAChatMessage(ChatRole.System, inputParamsAnswer.prompt.systemPrompt));
            }
            lMessages.Add(new SBAChatMessage(ChatRole.User, strContext));
            InputParametersChat inputParamChat = new InputParametersChat()
            {
                model = inputParamsAnswer.model,
                messagesHistory = lMessages,
                stream = false
            };
            Chat(inputParamChat);
            if (this.HasError()) return;

            this.JsonResponse.Set("answer", modelResponse.GetMessage().content);
            this.JsonResponse.Set("generationAPIExecutionTime", modelResponse.generationAPITime);
            this.JsonResponse.Set("queryExecutionTime", queryExecutionTime);

            if (inputParamsAnswer.debug) this.JsonResponse.Set("context", strContext);
            if (inputParamsAnswer.debug) this.JsonResponse.Set("GLLMContext", Json.Deserialize(JsonConvert.SerializeObject(context)));
            if (inputParamsAnswer.debug) this.JsonResponse.Set("res", jQueryResponse);
        }

        private bool CheckAppQuery(InputParametersAppQuery inputAppQuery)
        {
            string appName = inputAppQuery.app;
            if (String.IsNullOrEmpty(appName))
            {
                this.SetError(500, $"app is null or empty");
                return false;
            }
            this.app = CC.Current.AllApps.GetApp(inputAppQuery.app);
            if (app == null)
            {
                this.SetError(500, $"Cannot find app [{inputAppQuery.app}]");
                return false;
            }
                       

            if (!this.Method.Session.CheckAppACL(CC.Current, app))
            {
                this.SetError(500, $"Cannot access app [{inputAppQuery.app}]");
                return false;
            }

            string queryName = inputAppQuery.query.name;
            if (String.IsNullOrEmpty(queryName))
            {
                this.SetError(500, $"query is null or empty");
                return false;
            }
            this.ccQuery = CC.Current.WebServices.Get(queryName).AsQuery();
            if (ccQuery == null)
            {
                this.SetError(500, $"Cannot find query [{queryName}] [{JsonConvert.SerializeObject(inputAppQuery)}]");
                return false;
            }

            return true;
        }
    }
}
