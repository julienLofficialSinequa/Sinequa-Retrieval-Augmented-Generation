///////////////////////////////////////////////////////////
// Plugin OpenAI : file AzureOpenAITokenizer.cs
//

using System;
using System.Collections.Generic;
using System.Linq;
using TiktokenSharp;

namespace Sinequa.Plugin
{
    public static class GPTTokenizer
    {
        //gpt-3.5-turbo     => cl100k_base
        //gpt-4             => cl100k_base
        
        //test: antidisestablishmentarianism
        //gpt-3.5-turbo     => 6 tokens

        private static TikToken tikToken_GPT35 = TikToken.EncodingForModel("gpt-3.5-turbo");
        private static TikToken tikToken_GPT4 = TikToken.EncodingForModel("gpt-4");

        public static List<int> Encode(ModelType model, string text)
        {
            switch (model)
            {
                case ModelType.GPT35Turbo:
                    return tikToken_GPT35.Encode(text);
                case ModelType.GPT4_8K:
                case ModelType.GPT4_32K:
                    return tikToken_GPT4.Encode(text);
                default:
                    throw new NotImplementedException("model not supported");
            }
        }

        public static int Count(ModelType model, string text) => Encode(model, text).Count();

        public static List<int> Count(ModelType model, List<string> lText)=> lText.Select(_ => Count(model, _)).ToList();

    }
}
