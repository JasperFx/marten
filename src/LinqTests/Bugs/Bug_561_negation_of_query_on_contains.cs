using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_561_negation_of_query_on_contains: IntegrationContext
{
    public Bug_561_negation_of_query_on_contains(DefaultStoreFixture fixture) : base(fixture)
    {

    }

    protected override Task fixtureSetup()
    {
        var doc1 = new DocWithArrays { Strings = new string[] { "a", "b", "c" } };
        var doc2 = new DocWithArrays { Strings = new string[] { "c", "d", "e" } };
        var doc3 = new DocWithArrays { Strings = new string[] { "d", "e", "f" } };
        var doc4 = new DocWithArrays { Strings = new string[] { "g", "h", "i" } };

        theSession.Store(doc1, doc2, doc3, doc4);

        return theSession.SaveChangesAsync();
    }

    [Fact]
    public void negated_contains()
    {
        #region sample_negated-contains
        theSession.Query<DocWithArrays>().Count(x => !x.Strings.Contains("c"))
            .ShouldBe(2);
        #endregion
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

public class Bug_561_negation_of_query_on_contains_with_camel_casing: BugIntegrationContext
{
    public Bug_561_negation_of_query_on_contains_with_camel_casing()
    {
        StoreOptions(_ => _.UseDefaultSerialization(casing: Casing.CamelCase));

        var doc1 = new DocWithArrays { Strings = new string[] { "a", "b", "c" } };
        var doc2 = new DocWithArrays { Strings = new string[] { "c", "d", "e" } };
        var doc3 = new DocWithArrays { Strings = new string[] { "d", "e", "f" } };
        var doc4 = new DocWithArrays { Strings = new string[] { "g", "h", "i" } };

        TheSession.Store(doc1, doc2, doc3, doc4);

        TheSession.SaveChanges();
    }

    [Fact]
    public void negated_contains()
    {
        #region sample_negated-contains
        TheSession.Query<DocWithArrays>().Count(x => !x.Strings.Contains("c"))
            .ShouldBe(2);
        #endregion
    }

    [Fact]
    public void NotContainsInExpressionThrowsNotSupportedException()
    {
        TheSession.Query<DocWithArrays>().Count(x => x.Strings.Contains("d") && !x.Strings.Contains("c"))
            .ShouldBe(1);
    }

    [Fact]
    public void ExpressionWithNotContainsReturnsCorrectResults()
    {
        TheSession.Query<DocWithArrays>().Count(x => x.Strings.Contains("d") && !x.Strings.Contains("c"))
            .ShouldBe(1);
    }
}

public class DocWithArrays
{
    public Guid Id { get; set; }

    public int[] Numbers { get; set; }

    public string[] Strings { get; set; }

    public DateTime[] Dates { get; set; }
}