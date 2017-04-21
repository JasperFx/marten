using System;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Services.FullTextSearch;
using Xunit;

namespace Marten.Testing.Schema
{
    [Collection("DefaultSchema")]
    public class search_by_phrase_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void can_search_by_words()
        {
            theSession.DocumentStore.Schema.FullTextSearch.Search<Target>(c =>
            {
                c.By(t => t.String).UsingVector("string_tsvector");
            });

            var item = new Target {String = LoremIpsum(5, 10, 2, 2, 2)};
            theSession.Store(item);

            theSession.SaveChanges();

            var words = item.String.Split(' ').Take(2);

            var items = theSession.FullTextSearch<Target>(string.Join(" ", words));

            Assert.True(items.Any());
        }

        [Fact]
        public void can_search_by_multiple_fields()
        {
            theSession.DocumentStore.Schema.FullTextSearch.Search<Target>(c =>
            {
                c.By(t => t.String).By(t => t.AnotherString).UsingVector("two_string_tsvector");
            });

            var item = new Target { String = "First string", AnotherString = "Another string" };
            theSession.Store(item);

            theSession.SaveChanges();
            
            var items = theSession.FullTextSearch<Target>("string");
            var items2 = theSession.FullTextSearch<Target>("another");
            Assert.True(items.Any());
            Assert.True(items2.Any());
        }

        // From http://stackoverflow.com/questions/4286487/is-there-any-lorem-ipsum-generator-in-c
        private static string LoremIpsum(int minWords, int maxWords,
            int minSentences, int maxSentences,
            int numParagraphs) {

            var words = new[]{"lorem", "ipsum", "dolor", "sit", "amet", "consectetuer",
                "adipiscing", "elit", "sed", "diam", "nonummy", "nibh", "euismod",
                "tincidunt", "ut", "laoreet", "dolore", "magna", "aliquam", "erat"};

            var rand = new Random();
            var numSentences = rand.Next(maxSentences - minSentences)
                               + minSentences + 1;
            var numWords = rand.Next(maxWords - minWords) + minWords + 1;

            var result = new StringBuilder();

            for(var p = 0; p < numParagraphs; p++) {
                for(int s = 0; s < numSentences; s++) {
                    for(int w = 0; w < numWords; w++) {
                        if (w > 0) { result.Append(" "); }
                        result.Append(words[rand.Next(words.Length)]);
                    }
                    result.Append(". ");
                }
            }

            return result.ToString();
        }
    }

}