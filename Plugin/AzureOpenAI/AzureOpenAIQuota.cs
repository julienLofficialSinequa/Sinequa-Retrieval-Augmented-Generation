///////////////////////////////////////////////////////////
// Plugin OpenAI : file AzureOpenAIQuota.cs
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
using Azure.Core;
using System.Net.NetworkInformation;
using Newtonsoft.Json;

namespace Sinequa.Plugin
{
	
	public class AzureOpenAIQuota
	{
		[JsonIgnore]
        public const string QUOTA_TAG_NAME = "AzureOpenAIQuota";
        [JsonIgnore]
        public const string LAST_REST_TAG_NAME = "lastRest";
        [JsonIgnore]
        public const string TOKEN_COUNT_TAG_NAME = "tokenCount";

        [JsonIgnore]
        public DateTime lastRequest;
        public int tokenCount;
		public int periodTokens;
		public int resetHours;
        [JsonIgnore]
        public US userSettings;
        [JsonIgnore]
        public bool enabled = false;

        [JsonIgnore]
        public DateTime nextReset
		{
			get => lastRequest.AddHours(resetHours);
        }

        public DateTime lastResetUTC
        {
            get => lastRequest.ToUniversalTime();
        }
        
        public DateTime nextResetUTC
        {
            get => nextReset.ToUniversalTime();
        }

        public AzureOpenAIQuota(int periodTokens, int resetHours, US userSettings)
        {
            this.enabled = periodTokens != -1;

            this.periodTokens = periodTokens;
			this.resetHours = resetHours;
			this.userSettings = userSettings;

			userSettings.Load();
        }

        public bool Allowed()
        {
            Load();
			Reset();
			return tokenCount < periodTokens;
        }

		public bool Update(int addTokens)
		{
			tokenCount += addTokens;
            return Patch();
        }

		private bool Reset()
		{
			if (DateTime.UtcNow > nextReset)
			{
				tokenCount = 0;
                lastRequest = DateTime.UtcNow;
                return Patch();
            }
            return true;
        }

        private bool Exist()
		{
			return userSettings.Doc.EltExist(QUOTA_TAG_NAME);
        }

		private bool Create()
		{
            tokenCount = 0;
            lastRequest = DateTime.UtcNow;
            return Patch();
        }

		private bool Patch()
		{
            XDoc doc = userSettings.XDocLoad();
            doc.Elt(QUOTA_TAG_NAME, true).ValueSet(TOKEN_COUNT_TAG_NAME, tokenCount);
            doc.Elt(QUOTA_TAG_NAME, true).ValueSet(LAST_REST_TAG_NAME, lastRequest);
            return userSettings.XDocSave(doc);
        }

		private void Load()
		{
            if (!Exist())
            {
                Create();
                return;
            }

            XDoc elem = userSettings.Doc.Elt(QUOTA_TAG_NAME);
			lastRequest = elem.ValueDat(LAST_REST_TAG_NAME);
			tokenCount = elem.ValueInt(TOKEN_COUNT_TAG_NAME);
        }

    }

}
