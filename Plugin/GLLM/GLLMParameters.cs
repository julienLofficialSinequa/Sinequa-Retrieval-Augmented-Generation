using Azure.AI.OpenAI;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Sinequa.Common;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

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
            TokenCount,
            [EnumMember(Value = "Quota")]
            Quota,
            [EnumMember(Value = "Context")]
            Context,
            [EnumMember(Value = "Answer")]
            Answer
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

        [JsonProperty(Required = Required.Always)]
        public ModelParameters model { get; set; } = new ModelParameters();

        [JsonProperty]
        public bool promptProtection { get; set; } = true;

        [JsonProperty]
        public bool stream { get; set; } = false;
    }

    public class InputParametersTokensCount : InputParameters
    {
        [JsonProperty(Required = Required.Always)]
        public ModelName model { get; set; }

        [JsonProperty(Required = Required.Always)]
        public List<string> text { get; set; }

    }

    public class ModelParameters
    {
        [JsonProperty(Required = Required.Always)]
        public ModelName name { get; set; }

        ///////////////////////////////////////////////////////////////////////////
        //OpenAI ChatGPT + Google VertexAI Bison
        public double temperature { get; set; } = 0.7;
        public int generateTokens { get; set; } = 800;
        public double topP { get; set; } = 0.8;
        ///////////////////////////////////////////////////////////////////////////


        ///////////////////////////////////////////////////////////////////////////
        //OpenAI ChatGPT
        public double frequencyPenalty { get; set; } = 0;
        public double presencePenalty { get; set; } = 0;
        public int bestOf { get; set; } = 1;
        ///////////////////////////////////////////////////////////////////////////


        ///////////////////////////////////////////////////////////////////////////
        //Google VertexAI Bison
        public int topK { get; set; } = 40;
        public string context { get; set; } = "";
        public List<VertexAIBisonQueryInstanceExamples> examples = new List<VertexAIBisonQueryInstanceExamples>();
        ///////////////////////////////////////////////////////////////////////////
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

    public class InputParametersAppQuery : InputParameters
    {
        [JsonProperty(Required = Required.Always)]
        public string app { get; set; } = String.Empty;

        [JsonProperty(Required = Required.Always)]
        public InputParametersSearchQuery query { get; set; } = null;
    }

    public class InputParametersContext : InputParametersAppQuery
    {
        [JsonProperty(Required = Required.Always)]
        public InputParametersContextOptions contextOptions { get; set; } = new InputParametersContextOptions();
    }

    public class InputParametersAnswer : InputParametersAppQuery
    {
        [JsonProperty(Required = Required.Always)]
        public ModelParameters model { get; set; } = new ModelParameters();

        [JsonProperty(Required = Required.Always)]
        public InputPrompt prompt { get; set; } = null;

        [JsonProperty(Required = Required.Always)]
        public InputParametersContextOptions contextOptions { get; set; } = new InputParametersContextOptions();
    }

    public class InputPrompt
    {
        [JsonProperty(Required = Required.Always)]
        public string systemPrompt { get; set; } = Str.Empty;

        public string userBeforeContext { get; set; } = Str.Empty;

        public string userAfterContext { get; set; } = Str.Empty;
    }

    public class InputParametersSearchQuery
    {
        [JsonProperty(Required = Required.Always)]
        public string name { get; set; } = String.Empty;
    }

    public class InputParametersContextOptions
    {
        public enum ExtendPassage
        {
            [EnumMember(Value = "None")]
            None,
            [EnumMember(Value = "Sentence")]
            Sentence,
            [EnumMember(Value = "Passage")]
            Passage
        }

        public enum ContextStrategy
        {
            [EnumMember(Value = "TopPassagesByScore")]
            TopPassagesByScore,
            /*
            [EnumMember(Value = "TopDocumentsByPassagesScore")]
            TopDocumentsByPassagesScore,
            */
        }

        [JsonProperty(Required = Required.Always)]
        public ContextStrategy strategy { get; set; }

        public int topPassages { get; set; } = 5;

        public double topPassagesMinScore { get; set; } = 0.5;

        public ExtendPassage extendPassageMode { get; set; } = ExtendPassage.None;

        public int extendSentences = 0;

        public List<string> docColumns { get; set; } = new List<string>();

        //TODO

        //public int fillGaps = 500;

        //max tokens

    }
}
