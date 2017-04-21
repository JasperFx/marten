using Marten.Services.FullTextSearch;
using Marten.Testing.Documents;
using Marten.Util;
using Xunit;

namespace Marten.Testing.Schema
{
    public class search_map_Tests
    {     
        [Fact]
        public void map_tostring_explains_object()
        {
            var map = SearchMap.Register<SearchMe>(c =>
            {
                c.By(s => s.Property);
                c.By(s => s.Field);
            });

            var to_string = map.ToString();

            Assert.Contains("Property", to_string);
            Assert.Contains("Field", to_string);
        }

        class SearchMe
        {
            public object Property { get; set; }
            public object Field { get; set; }
        }
    }
}