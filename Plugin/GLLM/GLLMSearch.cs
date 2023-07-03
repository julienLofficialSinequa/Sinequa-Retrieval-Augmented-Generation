using System;
using Sinequa.Common;
using Sinequa.Search;
using Sinequa.Search.JsonMethods;
using Sinequa.Configuration.SBA;
using System.Diagnostics;

namespace Sinequa.Plugin
{
    
    public class GLLMSearch
	{
        private SearchSession _searchSession;
        private ICCApp _app;
        private Json _jQuery;
        private CCQuery _CCQuery;

        private Stopwatch sw = new Stopwatch();

        public GLLMSearch(ICCApp app, CCQuery ccQuery, SearchSession searchSession, Json jQuery)
        {
            _searchSession = searchSession;
            _app = app;
            _CCQuery = ccQuery;
            _jQuery = jQuery;
        }

        public Json ExecuteQuery(out long queryExecutionTime)
        {  
            var jquery = JQuery.NewQuery(_searchSession, Json.NewObject());
            jquery.JsonRequest.Set("app", _app.Name);
            jquery.JsonRequest.Set("query", _jQuery);
            
            sw.Start();
            jquery.Execute();
            queryExecutionTime = sw.ElapsedMilliseconds;
            sw.Reset();

            return jquery.JsonResponse;
        }

	}

}