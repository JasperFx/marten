using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shouldly;

namespace Marten.Testing.Linq.Compatibility.Support
{
    public class OrderedSelectComparison<T> : LinqTestCase
    {
        private readonly Func<IQueryable<Target>, IQueryable<T>> _selector;

        public OrderedSelectComparison(Func<IQueryable<Target>, IQueryable<T>> selector)
        {
            _selector = selector;
        }

        public override async Task Compare(IQuerySession session, Target[] documents)
        {
            var expected = _selector(documents.AsQueryable()).ToArray();

            var actual = (await (_selector(session.Query<Target>()).ToListAsync())).ToArray();

            assertSame(expected, actual);
        }

        private void assertSame(T[] expected, T[] actual)
        {
            actual.Length.ShouldBe(expected.Length, "The number of results");

            for (int i = 0; i < expected.Length; i++)
            {
                var expectedJson = JsonConvert.SerializeObject(expected[i]);
                var actualJson = JsonConvert.SerializeObject(actual[i]);
                
                if (!JToken.DeepEquals(JObject.Parse(expectedJson), JObject.Parse(actualJson)))
                {
                    // This would you would assume throw
                    actualJson.ShouldBe(expectedJson);
                }
            }
        }
    }
}