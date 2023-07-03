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
using System.Linq;

namespace Sinequa.Plugin
{

    public class SearchDocument
    {
        [JsonIgnore]
        public GLLMContext context { get; }

        public string id { get; }

        private Json _jDoc;

        public object jDoc
        {
            get => JsonConvert.DeserializeObject(Json.Serialize(_jDoc));
        }

        public List<NSPassage> NSPassages { get; } = new List<NSPassage>();

        public double passagesScore
        {
            get => NSPassages.Sum(_ => _.score);
        }

        public SearchDocument(GLLMContext context, string id, Json jDocs, bool debug = false)
        {
            this.context = context;
            this.id = id;
            this._jDoc = jDocs;
        }

        public void AddPassage(NSPassage passage)
        {
            NSPassages.Add(passage);
        }

        public string GetContext(List<NSPassage> lPassages, InputParametersContextOptions options)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(PreContext());
            sb.Append(PassagesContext(lPassages));
            sb.Append(PostContext());
            return sb.ToString();
        }

        private string PreContext()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<document");
            foreach (string column in context.options.docColumns)
            {
                string value = JsonPath.GetValue(_jDoc, $"$.{column}");
                if (!String.IsNullOrEmpty(column)) sb.Append($" {column}=\"{value}\"");
            }
            sb.Append(">");
            return sb.ToString();
        }

        private string PostContext()
        {
            return "</document>";
        }

        private string PassagesContext(List<NSPassage> lPassages)
        {
            StringBuilder sb = new StringBuilder();

            var r = TextChunck.GetDocumentPassagesChunks(context.appName, context.queryName, this);

            foreach (TextChunck t in r) sb.Append(t.text);

            return sb.ToString();
        }
    }


    public class TextChunck
    {
        public int length;
        public int offset;
        public string text;

        public static List<TextChunck> GetDocumentPassagesChunks(string appName, string queryName, SearchDocument doc)
        {
            var textChunk = JsonMethod.NewMethod(JsonMethodType.DocumentTextChunks, doc.context.session);

            List<(int offset, int length)> lTextChunksPositions = doc.NSPassages.Select(_ => (_.textLocationStart, _.textLength)).ToList();

            textChunk.JsonRequest = GetPayload(appName, queryName, doc.id, doc.context.options, lTextChunksPositions);
            textChunk.Execute();
            Json response = textChunk.JsonResponse.Get("chunks");

            return JsonConvert.DeserializeObject<List<TextChunck>>(Json.Serialize(response));
        }

        private static Json GetPayload(string appName, string queryName, string docId, InputParametersContextOptions options, List<(int offset, int length)> lTextChunksPositions)
        {
            JsonObject query = new JsonObject();
            query.Set("name", queryName);


            JsonObject payload = new JsonObject();
            payload.Set("app", appName);
            payload.Set("query", query);
            payload.Set("id", docId);

            JsonArray jArrTextChunks = new JsonArray();
            foreach ((int offset, int length) TCP in lTextChunksPositions)
            {
                JsonObject jTextChunks = new JsonObject();
                jTextChunks.Set("offset", TCP.offset);
                jTextChunks.Set("length", TCP.length);
                jArrTextChunks.EltAdd(jTextChunks);
            }
            payload.Set("textChunks", jArrTextChunks);

            if (options.extendPassageMode == InputParametersContextOptions.ExtendPassage.Sentence)
            {
                if (options.extendSentences > 0)
                {
                    payload.Set("leftSentencesCount", options.extendSentences);
                    payload.Set("rightSentencesCount", options.extendSentences);
                }
            }

            payload.SetArray("highlights");

            return payload;
        }
    }

    public class NSPassage
    {
        [JsonProperty(PropertyName = "id", Required = Required.Always)]
        public int passageId { get; set; }
        [JsonProperty(Required = Required.Always)]
        public double score { get; set; }
        [JsonProperty(Required = Required.Always)]
        private List<int> location { get; set; } = new List<int>();
        public int textLocationStart
        {
            get => location[0];
        }
        public int textLength
        {
            get => location[1];
        }
        public int textLocationEnd
        {
            get => textLocationStart + textLength;
        }
        [JsonProperty(PropertyName = "recordId", Required = Required.Always)]
        public string documentId { get; set; }

        public int rank;

        [JsonIgnore]
        public SearchDocument doc;

    }

}
