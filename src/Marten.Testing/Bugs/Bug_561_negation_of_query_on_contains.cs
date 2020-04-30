using System;
using System.Linq;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_561_negation_of_query_on_contains: IntegrationContextWithIdentityMap<NulloIdentityMap>
    {
        public Bug_561_negation_of_query_on_contains(DefaultStoreFixture fixture) : base(fixture)
        {
            var doc1 = new DocWithArrays { Strings = new string[] { "a", "b", "c" } };
            var doc2 = new DocWithArrays { Strings = new string[] { "c", "d", "e" } };
            var doc3 = new DocWithArrays { Strings = new string[] { "d", "e", "f" } };
            var doc4 = new DocWithArrays { Strings = new string[] { "g", "h", "i" } };

            theSession.Store(doc1, doc2, doc3, doc4);

            theSession.SaveChanges();
        }

        [Fact]
        public void negated_contains()
        {
            // SAMPLE: negated-contains
            theSession.Query<DocWithArrays>().Count(x => !x.Strings.Contains("c"))
                .ShouldBe(2);
            // ENDSAMPLE
        }

        [Fact]
        public void NotContainsInExpressionThrowsNotSupportedException()
        {
            theSession.Query<DocWithArrays>().Count(x => x.Strings.Contains("d") && !x.Strings.Contains("c"))
                .ShouldBe(1);
        }

        [Fact]
        public void ExpressionWithNotContainsReturnsCorrectResults()
        {
            theSession.Query<DocWithArrays>().Count(x => x.Strings.Contains("d") && !x.Strings.Contains("c"))
                .ShouldBe(1);
        }
    }

    public class Bug_561_negation_of_query_on_contains_with_camel_casing: IntegrationContextWithIdentityMap<NulloIdentityMap>
    {
        public Bug_561_negation_of_query_on_contains_with_camel_casing(DefaultStoreFixture fixture): base(fixture)
        {
            StoreOptions(_ => _.UseDefaultSerialization(casing: Casing.CamelCase));

            var doc1 = new DocWithArrays { Strings = new string[] { "a", "b", "c" } };
            var doc2 = new DocWithArrays { Strings = new string[] { "c", "d", "e" } };
            var doc3 = new DocWithArrays { Strings = new string[] { "d", "e", "f" } };
            var doc4 = new DocWithArrays { Strings = new string[] { "g", "h", "i" } };

            theSession.Store(doc1, doc2, doc3, doc4);

            theSession.SaveChanges();
        }

        [Fact]
        public void negated_contains()
        {
            // SAMPLE: negated-contains
            theSession.Query<DocWithArrays>().Count(x => !x.Strings.Contains("c"))
                .ShouldBe(2);
            // ENDSAMPLE
        }

        [Fact]
        public void NotContainsInExpressionThrowsNotSupportedException()
        {
            theSession.Query<DocWithArrays>().Count(x => x.Strings.Contains("d") && !x.Strings.Contains("c"))
                .ShouldBe(1);
        }

        [Fact]
        public void ExpressionWithNotContainsReturnsCorrectResults()
        {
            theSession.Query<DocWithArrays>().Count(x => x.Strings.Contains("d") && !x.Strings.Contains("c"))
                .ShouldBe(1);
        }
    }

    public class DocWithArrays
    {
        public Guid Id;

        public int[] Numbers;

        public string[] Strings;

        public DateTime[] Dates;
    }
}
