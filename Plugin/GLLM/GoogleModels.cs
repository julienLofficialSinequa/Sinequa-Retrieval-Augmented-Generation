using System;
using System.Collections.Generic;
using System.Text;
using Sinequa.Common;
using Sinequa.Configuration;
using Azure.AI.OpenAI;
using Newtonsoft.Json.Converters;
using System.Linq;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Runtime.Serialization;
using Sinequa.Search;
using Sinequa.Web;
using Sinequa.Search.JsonMethods;
using TiktokenSharp;
using Google.Apis.Auth.OAuth2;
using System.IO;

namespace Sinequa.Plugin
{

    public abstract class GoogleVertexAIModel : GLLMModel
    {
        public override ModelProvider provider => ModelProvider.Google;

        [JsonIgnore]
        public abstract string envVarAPIEndPoint { get; }
        [JsonIgnore]
        public abstract string envVarAPIProjectID { get; }
        [JsonIgnore]
        public abstract string envVarAPIModelID { get; }
        [JsonIgnore]
        public abstract string envVarOAuthJson { get; }
        [JsonIgnore]
        public abstract string envVarPromptProtection { get; }

        public double topKMin { get; }
        public double topKMax { get; }

        public const double TEMP_MIN = 0;
        public const double TEMP_MAX = 1;
        public const int GEN_TOKEN_MIN = 1;
        public const int GEN_TOKEN_MAX = 1_024;
        public const double TOP_P_MIN = 0;
        public const double TOP_P_MAX = 1;
        public const double TOP_K_MIN = 1;
        public const double TOP_K_MAX = 40;

        internal string APIEndPoint = "";
        internal string APIProjectID = "";
        internal string APIModelID = "";
        internal string OAuthJson = "";
        internal VertexAIBisonPayload postPayload;

        public override Json postBody => Json.Deserialize(JsonConvert.SerializeObject(this.postPayload));

        protected GoogleVertexAIModel(SearchSession session, ModelParameters modelParams) : base(session, modelParams, TEMP_MIN, TEMP_MAX, GEN_TOKEN_MIN, GEN_TOKEN_MAX, TOP_P_MIN, TOP_P_MAX)
        {
            this.topKMin = TOP_K_MIN;
            this.topKMax = TOP_K_MAX;
        }

        public override bool CheckInputParams(out string errorMessage)
        {
            errorMessage = "";

            //temperature
            if (parameters.temperature < temperatureMin || parameters.temperature > temperatureMax) errorMessage = $"model.temperature must be between {temperatureMin} and {temperatureMax}";
            //generateTokens
            if (parameters.generateTokens < generateTokensMin || parameters.generateTokens > generateTokensMax) errorMessage = $"model.generateTokens must be between {generateTokensMin} and {generateTokensMax}";
            //topP
            if (parameters.topP < topPMin || parameters.topP > topPMax) errorMessage = $"model.topP must be between {topPMin} and {topPMax}";
            //topK
            if (parameters.topK < topKMin || parameters.topK > topKMax) errorMessage = $"model.topK must be between {topKMin} and {topKMax}";

            return string.IsNullOrEmpty(errorMessage);
        }

        public override bool LoadEnvVars(out string errorMessage)
        {
            errorMessage = "";

            APIEndPoint = CC.Current.EnvVars.Resolve(envVarAPIEndPoint);
            if (Str.EQ(APIEndPoint, envVarAPIEndPoint)) errorMessage = $"Missing environment variable [{envVarAPIEndPoint}]";
            APIProjectID = CC.Current.EnvVars.Resolve(envVarAPIProjectID);
            if (Str.EQ(APIProjectID, envVarAPIProjectID)) errorMessage = $"Missing environment variable [{envVarAPIProjectID}]";
            APIModelID = CC.Current.EnvVars.Resolve(envVarAPIModelID);
            if (Str.EQ(APIModelID, envVarAPIModelID)) errorMessage = $"Missing environment variable [{envVarAPIModelID}]";
            OAuthJson = CC.Current.EnvVars.Resolve(envVarOAuthJson);
            if (Str.EQ(OAuthJson, envVarOAuthJson)) errorMessage = $"Missing environment variable [{envVarOAuthJson}]";
            promptProtection = CC.Current.EnvVars.Resolve(envVarPromptProtection);
            if (Str.EQ(promptProtection, envVarPromptProtection)) promptProtection = null;

            return string.IsNullOrEmpty(errorMessage);
        }

        public override GLLMResponse QueryAPI(out int HTTPCode, out string errorMessage)
        {
            sw.Restart();
            HTTPCode = 0;
            errorMessage = "";

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GetBearerToken());

                string googleVertexEndpoint = $"https://{APIEndPoint}/v1/projects/{APIProjectID}/locations/us-central1/publishers/google/models/{APIModelID}:predict";

                VertexAIBisonPayload payload = new VertexAIBisonPayload(parameters, messages);
                this.postPayload = payload;

                var jPayload = JsonConvert.SerializeObject(payload);
                StringContent postParams = new StringContent(jPayload, Encoding.UTF8, "application/json");
                HttpResponseMessage response = client.PostAsync(googleVertexEndpoint, postParams).Result;

                if (!response.IsSuccessStatusCode)
                {
                    HTTPCode = (int)response.StatusCode;
                    errorMessage = response.Content.ReadAsStringAsync().Result;
                    return null;
                }

                GLLMResponse res = new GLLMResponse(this, sw.ElapsedMilliseconds);
                res.SetResponse(JsonConvert.DeserializeObject<VertexAIBisonResponse>(response.Content.ReadAsStringAsync().Result));

                return res;
            }
        }

        private string GetBearerToken()
        {
            string jsonKeyFilePath = @"C:\_sinequa_cert\google\consultants-169215-55dcb28be0df.json";

            using (var stream = new FileStream(jsonKeyFilePath, FileMode.Open, FileAccess.Read))
            {
                return GoogleCredential
                    .FromStream(stream)
                    .CreateScoped("https://www.googleapis.com/auth/cloud-platform")
                    .UnderlyingCredential
                    .GetAccessTokenForRequestAsync().Result;
            }
        }

        //TODO - PaLM tokenizer ?
        private static TikToken tikToken_GPT35 = TikToken.EncodingForModel("gpt-3.5-turbo");

        public override int Tokenizer(string str, out int HTTPCode, out string errorMessage)
        {
            HTTPCode = 200;
            errorMessage = "";
            return tikToken_GPT35.Encode(str).Count;
        }
    }

    public class GoogleVertexAIBison_001 : GoogleVertexAIModel
    {
        public static GoogleVertexAIBison_001 GetDefaultInstance(SearchSession session)
        {
            return new GoogleVertexAIBison_001(session, null);
        }

        public GoogleVertexAIBison_001(SearchSession session, ModelParameters modelParams) : base(session, modelParams)
        {
        }

        public override string envVarAPIEndPoint => "%%google-vertexai-endpoint%%";
        public override string envVarAPIProjectID => "%%google-vertexai-project-id%%";
        public override string envVarAPIModelID => "%%google-vertexai-model-id%%";
        public override string envVarPromptProtection => "%%google-vertexai-prompt-protection%%";
        public override string envVarOAuthJson => "%%google-vertexai-service-account-json-credentials%%";

        public override ModelName name => ModelName.GoogleVertex_Chat_Bison_001;

        public override string displayName => "Google - PaLM - 4K Tokens";

        //TBD - model size ~8K
        public override int size => 4_096;

        public override bool eventStream => false;

        public override void StreamQueryAPI(JsonMethod method, GLLMQuota quota)
        {
            throw new NotImplementedException();
        }
    }

    public class VertexAIBisonResponse
    {
        public List<VertexAIBisonPrediction> predictions;
        public VertexAIBisonSafetyAttributes safetyAttributes;
        public string deployedModelId;
        public string model;
        public string modelDisplayName;
        public string modelVersionId;
    }

    public class VertexAIBisonPrediction
    {
        public List<VertexAIChatMessage> candidates;
    }

    public class VertexAIBisonSafetyAttributes
    {
        public List<string> categories;
        public bool blocked;
        public List<double> scores;
    }

    public class VertexAIChatMessage
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public enum VertexAIBisonAuthor
        {
            [EnumMember(Value = "User")]
            User,
            [EnumMember(Value = "Bot")]
            Bot
        }

        public VertexAIBisonAuthor author;
        public string content;

        [JsonConstructor]
        public VertexAIChatMessage(VertexAIBisonAuthor author, string content)
        {
            this.author = author;
            this.content = content;
        }

        public VertexAIChatMessage(ChatRole role, string content)
        {
            this.author = RoleToAuthor(role);
            this.content = content;
        }

        [JsonIgnore]
        public ChatRole role
        {
            get => AuthorToRole(this.author);
        }

        private VertexAIBisonAuthor RoleToAuthor(ChatRole role) => role == ChatRole.User ? VertexAIBisonAuthor.User : VertexAIBisonAuthor.Bot;

        private ChatRole AuthorToRole(VertexAIBisonAuthor author) => author == VertexAIBisonAuthor.User ? ChatRole.User : ChatRole.Assistant;
    }

    public class VertexAIBisonPayload
    {
        public List<VertexAIBisonQueryInstance> instances;
        public VertexAIBisonQueryParameters parameters;

        public VertexAIBisonPayload(ModelParameters modelParams, List<SBAChatMessage> SBAChatMessages)
        {
            parameters = new VertexAIBisonQueryParameters(modelParams);

            this.instances = new List<VertexAIBisonQueryInstance>() {
                new VertexAIBisonQueryInstance(modelParams, SBAChatMessages)
            };
        }
    }

    public class VertexAIBisonQueryInstance
    {
        public string context;
        public List<VertexAIBisonQueryInstanceExamples> examples;
        public List<VertexAIChatMessage> messages;

        public VertexAIBisonQueryInstance(ModelParameters modelParams, List<SBAChatMessage> SBAChatMessages)
        {
            this.context = modelParams.context;
            this.examples = modelParams.examples;
            this.messages = new List<VertexAIChatMessage>();

            ChatRole previousRole;
            StringBuilder sbContent = new StringBuilder();

            foreach (SBAChatMessage SBAChatMessage in SBAChatMessages)
            {
                //first - init previous role
                if (SBAChatMessages.First() == SBAChatMessage)
                {
                    previousRole = SBAChatMessage.role;
                }

                //system role is changed to user role - TBD
                if (SBAChatMessage.role == ChatRole.System)
                {
                    sbContent.AppendLine(SBAChatMessage.content);
                    previousRole = ChatRole.User;
                    continue;
                }

                //aggregate content
                if (previousRole == SBAChatMessage.role)
                {
                    sbContent.AppendLine(SBAChatMessage.content);
                }
                //role change, add to messages
                else
                {
                    messages.Add(new VertexAIChatMessage(previousRole, sbContent.ToString()));

                    sbContent.Clear();
                    sbContent.AppendLine(SBAChatMessage.content);
                    previousRole = SBAChatMessage.role;
                }

                //last - add to messages
                if (SBAChatMessages.Last() == SBAChatMessage)
                {
                    messages.Add(new VertexAIChatMessage(previousRole, sbContent.ToString()));
                }
            }
        }
    }

    public class VertexAIBisonQueryInstanceExamples
    {
        public VertexAIChatMessage input;
        public VertexAIChatMessage output;

        public VertexAIBisonQueryInstanceExamples(string input, string output)
        {
            this.input = new VertexAIChatMessage(VertexAIChatMessage.VertexAIBisonAuthor.User, input);
            this.output = new VertexAIChatMessage(VertexAIChatMessage.VertexAIBisonAuthor.Bot, output);
        }
    }

    public class VertexAIBisonQueryParameters
    {
        public VertexAIBisonQueryParameters(ModelParameters modelParams)
        {
            this.temperature = modelParams.temperature;
            this.maxOutputTokens = modelParams.generateTokens;
            this.topP = modelParams.topP;
            this.topK = modelParams.topK;
        }

        public double temperature;
        public int maxOutputTokens;
        public double topP;
        public int topK;
    }

}
