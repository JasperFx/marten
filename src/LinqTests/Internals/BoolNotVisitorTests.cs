using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Harness;
using Xunit.Abstractions;

namespace LinqTests.Internals;

public class BoolNotVisitorTests : OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public class TestClass
    {
        public TestClass()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }
        public bool Flag { get; set; }
    }

    [Fact]
    public async Task when_doc_with_bool_false_should_return_records()
    {
        var docWithFlagFalse = new TestClass();

        theSession.Store(docWithFlagFalse);
        await theSession.SaveChangesAsync();

        using var s = theStore.QuerySession();
        var items = s.Query<TestClass>().Where(x => !x.Flag).ToList();

        Assert.Single(items);
        Assert.Equal(docWithFlagFalse.Id, items[0].Id);
    }

    [Fact]
    public async Task when_doc_with_bool_false_with_serializer_default_value_handling_null_should_return_records()
    {
        var serializer = new JsonNetSerializer();
        serializer.Configure(s =>
        {
            s.DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore;
        });

        StoreOptions(x => x.Serializer(serializer));

        // Note: with serializer settings DefaultValueHandling.Ignore, serialized JSON won't have Flag property
        var docWithFlagFalse = new TestClass();

        theSession.Store(docWithFlagFalse);
        await theSession.SaveChangesAsync();

        using (var s = theStore.QuerySession())
        {
            var items = s.Query<TestClass>().Where(x => !x.Flag).ToList();

            Assert.Single(items);
            Assert.Equal(docWithFlagFalse.Id, items[0].Id);
        }
    }

    public BoolNotVisitorTests(ITestOutputHelper output)
    {
        _output = output;
    }
}
