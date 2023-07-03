///////////////////////////////////////////////////////////
// Plugin GLLM : file GLLMContext.cs
//

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
using System.Linq;

namespace Sinequa.Plugin
{
    public class GLLMContext
    {
        private Json _jQueryResponse;
        public InputParametersContextOptions options { get; }

        [JsonIgnore]
        public SearchSession session { get; }
        public string appName { get; }
        public string queryName { get; }

        public List<SearchDocument> searchDocuments = new List<SearchDocument>();

        private GLLMContext(Json jQueryResponse, InputParametersContextOptions options, SearchSession session, string appName, string queryName)
        {
            _jQueryResponse = jQueryResponse;
            this.options = options;
            this.session = session;
            this.appName = appName;
            this.queryName = queryName;
        }

        public void Init()
        {
            LoadTopPassages();
        }

        private void LoadTopPassages()
        {
            if (_jQueryResponse.EltExist("topPassages") && _jQueryResponse.Elt("topPassages").EltExist("passages") && _jQueryResponse.Elt("topPassages").Elt("passages").IsArray())
            {
                List<NSPassage> lPassages = JsonConvert.DeserializeObject<List<NSPassage>>(Json.Serialize(_jQueryResponse.Elt("topPassages").Elt("passages")));

                int i = 0;
                foreach (NSPassage passage in lPassages)
                {
                    SearchDocument doc;
                    if (!searchDocuments.Exists(_ => Str.EQNC(_.id, passage.documentId)))
                    {
                        doc = new SearchDocument(
                            this,
                            passage.documentId,
                            JsonPath.GetJson(_jQueryResponse, $"$.records[?(@.id==\"{passage.documentId}\")]")
                        );
                        searchDocuments.Add(doc);
                    }
                    doc = searchDocuments.Single(_ => Str.EQNC(_.id, passage.documentId));

                    passage.rank = i; i++;
                    passage.doc = doc;

                    doc.AddPassage(passage);
                }
            }
        }

        public static GLLMContext FromSearch(Json jQueryResponse, InputParametersContextOptions options, SearchSession session, string appName, string queryName)
        {
            GLLMContext context = new GLLMContext(jQueryResponse, options, session, appName, queryName);
            context.Init();

            return context;
        }

        public string GetDocumentsContext()
        {
            StringBuilder sb = new StringBuilder();

            switch (options.strategy)
            {
                case InputParametersContextOptions.ContextStrategy.TopPassagesByScore:
                    sb.AppendLine(ContextFromTopPassagesByScore());
                    break;
                /*
                case InputParametersContextOptions.ContextStrategy.TopDocumentsByPassagesScore:
                    sb.AppendLine(ContextTopDocumentsByPassagesScore());
                    break
                */
                default:
                    throw new NotImplementedException("ContextStrategy not implemented");
            }

            return sb.ToString();
        }

        public string GetPromptContext(InputPrompt prompt)
        {
            StringBuilder sb = new StringBuilder();

            if (!String.IsNullOrEmpty(prompt.userBeforeContext))
                sb.AppendLine(prompt.userBeforeContext);

            sb.AppendLine(GetDocumentsContext());

            if (!String.IsNullOrEmpty(prompt.userAfterContext)) sb.AppendLine(prompt.userAfterContext);

            return sb.ToString();
        }

        private string ContextFromTopPassagesByScore()
        {
            StringBuilder sb = new StringBuilder();

            foreach (SearchDocument doc in searchDocuments)
            {
                List<NSPassage> lPassages = doc.NSPassages.Where(_ => _.rank < options.topPassages).ToList();

                if (lPassages.Count > 0)
                {
                    sb.AppendLine(doc.GetContext(lPassages, options));
                }
            }

            return sb.ToString();
        }

        private string ContextTopDocumentsByPassagesScore()
        {
            StringBuilder sb = new StringBuilder();

            return sb.ToString();
        }
    }

}
