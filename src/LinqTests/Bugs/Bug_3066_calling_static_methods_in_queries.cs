using System.Linq;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_3066_calling_static_methods_in_queries : BugIntegrationContext
{
    private readonly ITestOutputHelper _testOutputHelper;

    public Bug_3066_calling_static_methods_in_queries(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public void GettingPropertyFromTypedObjectCreatedFromStaticMethod()
    {
        StoreOptions(opts =>
        {
            opts.RegisterDocumentType<Account>();
        });

        var queryPlan = theSession.Query<Account>().Where(x => x.Id == AccountId.New("123").Value).Explain();
        _testOutputHelper.WriteLine(queryPlan.Command.CommandText);
    }

    public record AccountId(string Value)
    {
        public static AccountId New(string value) => new AccountId(value);
    }
}
