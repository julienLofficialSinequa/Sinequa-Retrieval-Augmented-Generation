///////////////////////////////////////////////////////////
// Plugin OpenAI : file AzureOpenAIParameters.cs
//

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using Azure.AI.OpenAI;

namespace Sinequa.Plugin
{

    public class InputParameters
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public enum Action
        {
            [EnumMember(Value = "ListModels")]
            ListModels,
            [EnumMember(Value = "Chat")]
            Chat,
            [EnumMember(Value = "TokenCount")]
            TokenCount
        }

        [JsonProperty(Required = Required.Always)]
        public Action action { get; set; }

        [JsonProperty]
        public bool debug { get; set; } = false;

    }

    public class InputParametersChat : InputParameters
    {
        [JsonProperty(Required = Required.Always)]
        public List<SBAChatMessage> messagesHistory { get; set; }

        [JsonProperty]
        public ModelParameters model { get; set; } = new ModelParameters();

        [JsonProperty]
        public bool promptProtection { get; set; } = true;
    }

    public class InputParametersTokensCount : InputParameters
    {
        [JsonProperty(Required = Required.Always)]
        public ModelType model { get; set; }

        [JsonProperty(Required = Required.Always)]
        public List<string> text { get; set; }

    }

    public class ModelParameters
    {
        [JsonProperty(Required = Required.Always)]
        public ModelType name { get; set; }

        public double temperature { get; set; } = 0.7;

        public int generateTokens { get; set; } = 800;

        public double frequencyPenalty { get; set; } = 0;

        public double presencePenalty { get; set; } = 0;

        public double topP { get; set; } = 0.95;

        public int bestOf { get; set; } = 1;

    }

    public class ChatAttachment
    {
        public string recordId { get; set; }
        public string queryStr { get; set; }
        public string text { get; set; }
        public string type { get; set; }
        public int offset { get; set; }
        public int length { get; set; }
        public int sentencesBefore { get; set; }
        public int sentencesAfter { get; set; }
        public int tokenCount { get; set; }
    }

    public class SBAChatMessage
    {
        [JsonProperty(Required = Required.Always)]
        [JsonIgnore]
        public ChatRole role { get; set; }

        [JsonProperty("role")]
        public string sRole
        {
            get => role.Label;
        }

        [JsonProperty(Required = Required.Always)]
        public string content { get; set; }

        public bool display { get; set; }

        public int tokens { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public ChatAttachment attachment { get; set; }

        public SBAChatMessage(ChatRole role, string content, bool display = false)
        {
            this.role = role;
            this.content = content;
            this.display = display;
        }

        public ChatMessage ToChatMessage()
        {
            return new ChatMessage(role, content);
        }
    }
}
