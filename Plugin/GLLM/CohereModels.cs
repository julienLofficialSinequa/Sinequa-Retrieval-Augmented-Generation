using System;
using System.Collections.Generic;
using System.Text;
using Sinequa.Common;
using Sinequa.Configuration;
using Sinequa.Plugins;
using Sinequa.Connectors;
using Sinequa.Indexer;
using Sinequa.Search;
using Newtonsoft.Json;
using Sinequa.Search.JsonMethods;
using System.Net.Http.Headers;
using System.Net.Http;
using Newtonsoft.Json.Converters;
using System.Runtime.Serialization;

namespace Sinequa.Plugin
{

    public abstract class CohereModels : GLLMModel
    {
        public override ModelProvider provider => ModelProvider.Cohere;

        [JsonIgnore]
        public abstract string envVarAPIEndPointGenerate { get; }
        [JsonIgnore]
        public abstract string envVarAPIEndPointTokenizer { get; }
        [JsonIgnore]
        public abstract string envVarAPIKey { get; }
        [JsonIgnore]
        public abstract string envVarPromptProtection { get; }

        public double topKMin { get; }
        public double topKMax { get; }

        public const double TEMP_MIN = 0;
        public const double TEMP_MAX = 2;
        public const int GEN_TOKEN_MIN = 1;
        public const int GEN_TOKEN_MAX = 4_095;
        public const double TOP_K_MIN = 0;
        public const double TOP_K_MAX = 500;

        internal string APIEndPointGenerate = "";
        internal string APIEndPointTokenizer = "";
        internal string APIKey = "";

        internal CohereCommandPayload postPayload;

        public override Json postBody => Json.Deserialize(JsonConvert.SerializeObject(this.postPayload));

        public CohereModels(SearchSession session, ModelParameters modelParams) : base(session, modelParams, TEMP_MIN, TEMP_MAX, GEN_TOKEN_MIN, GEN_TOKEN_MAX, -1, -1)
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
            //topK
            if (parameters.topK < topKMin || parameters.topK > topKMax) errorMessage = $"model.topK must be between {topKMin} and {topKMax}";

            return string.IsNullOrEmpty(errorMessage);
        }

        public override bool LoadEnvVars(out string errorMessage)
        {
            errorMessage = "";

            APIEndPointGenerate = CC.Current.EnvVars.Resolve(envVarAPIEndPointGenerate);
            if (Str.EQ(APIEndPointGenerate, envVarAPIEndPointGenerate)) errorMessage = $"Missing environment variable [{envVarAPIEndPointGenerate}]";
            APIEndPointTokenizer = CC.Current.EnvVars.Resolve(envVarAPIEndPointTokenizer);
            if (Str.EQ(APIEndPointTokenizer, envVarAPIEndPointTokenizer)) errorMessage = $"Missing environment variable [{envVarAPIEndPointTokenizer}]";
            APIKey = CC.Current.EnvVars.Resolve(envVarAPIKey);
            if (Str.EQ(APIKey, envVarAPIKey)) errorMessage = $"Missing environment variable [{envVarAPIKey}]";
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
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", APIKey);

                string cohereEndpoint = APIEndPointGenerate;

                CohereCommandPayload payload = new CohereCommandPayload(parameters, messages);
                this.postPayload = payload;

                var jPayload = JsonConvert.SerializeObject(postPayload);
                StringContent postParams = new StringContent(jPayload, Encoding.UTF8, "application/json");
                HttpResponseMessage response = client.PostAsync(cohereEndpoint, postParams).Result;

                if (!response.IsSuccessStatusCode)
                {
                    HTTPCode = (int)response.StatusCode;
                    errorMessage = response.Content.ReadAsStringAsync().Result;
                    return null;
                }

                GLLMResponse res = new GLLMResponse(this, sw.ElapsedMilliseconds);
                res.SetResponse(JsonConvert.DeserializeObject<CohereCommandResponse>(response.Content.ReadAsStringAsync().Result));

                return res;
            }
        }

        public override int Tokenizer(string str, out int HTTPCode, out string errorMessage)
        {
            HTTPCode = 0;
            errorMessage = "";

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", APIKey);

                string cohereEndpoint = APIEndPointTokenizer;

                CohereTokenizerPaylod payload = new CohereTokenizerPaylod(this.name, str);

                var jPayload = JsonConvert.SerializeObject(payload);
                StringContent postParams = new StringContent(jPayload, Encoding.UTF8, "application/json");
                HttpResponseMessage response = client.PostAsync(cohereEndpoint, postParams).Result;

                if (!response.IsSuccessStatusCode)
                {
                    HTTPCode = (int)response.StatusCode;
                    errorMessage = response.Content.ReadAsStringAsync().Result;
                    return 0;
                }

                CohereTokenizerResponse res = JsonConvert.DeserializeObject<CohereTokenizerResponse>(response.Content.ReadAsStringAsync().Result);

                return res.tokens.Count;
            }
        }

    }

    public class CohereGeneration : CohereModels
    {
        public static CohereGeneration GetDefaultInstance(SearchSession session)
        {
            return new CohereGeneration(session, new ModelParameters() { name = ModelName.Cohere_Command_XL_Beta });
        }

        public CohereGeneration(SearchSession session, ModelParameters modelParams) : base(session, modelParams)
        {
        }

        public override string envVarAPIEndPointGenerate => "%%cohere-generate-endpoint%%";
        public override string envVarAPIEndPointTokenizer => "%%cohere-tokenizer-endpoint%%";
        public override string envVarAPIKey => "%%cohere-generate-api-key%%";
        public override string envVarPromptProtection => "%%cohere-generate-prompt-protection%%";

        public override ModelName name => ModelName.Cohere_Command_XL_Beta;

        public override string displayName => "Cohere - Command XL Beta - 4K Tokens";

        //TBD - model size ~8K
        public override int size => 4_000;

        public override bool eventStream => false;

        public override void StreamQueryAPI(JsonMethod method, GLLMQuota quota)
        {
            throw new NotImplementedException();
        }
    }

    public class CohereCommandPayload
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public enum GenerateLikelihoodMode
        {
            [EnumMember(Value = "NONE")]
            None,
            [EnumMember(Value = "GENERATION")]
            Generation
        }

        public ModelName model { get; }
        public string prompt { get; } = null;
        public int max_tokens { get; } = 800;
        public double temperature { get; } = 0.9;
        public int k { get; } = 0;
        public List<string> stop_sequences { get; } = new List<string>();
        public GenerateLikelihoodMode return_likelihoods { get; }

        public CohereCommandPayload(ModelParameters modelParams, List<SBAChatMessage> SBAChatMessages)
        {
            this.model = modelParams.name;
            StringBuilder sb = new StringBuilder();
            foreach (SBAChatMessage SBAChatMessage in SBAChatMessages)
            {
                sb.AppendLine(SBAChatMessage.content);
            }
            this.prompt = sb.ToString();
            this.max_tokens = modelParams.generateTokens;
            this.temperature = modelParams.temperature;
            this.k = modelParams.topK;
            this.return_likelihoods = GenerateLikelihoodMode.None;
        }
    }

    public class CohereCommandResponse
    {
        public string id { get; set; }
        public List<CohereCommandGeneration> generations { get; set; }
        public string prompt { get; set; }
    }

    public class CohereCommandGeneration
    {
        public string id { get; set; }
        public string text { get; set; }
    }

    public class CohereTokenizerPaylod
    {
        public CohereTokenizerPaylod(ModelName model, string text)
        {
            this.model = model;
            this.text = text;
        }

        public ModelName model { get; set; }

        public string text { get; set; }
    }

    public class CohereTokenizerResponse
    {
        public List<int> tokens;
        public List<string> token_strings;
    }
}

