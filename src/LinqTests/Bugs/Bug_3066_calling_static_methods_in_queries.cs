using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
namespace LinqTests.Bugs;

public class Bug_3066_calling_static_methods_in_queries : BugIntegrationContext
{

    [Fact]
    public async Task GettingPropertyFromTypedObjectCreatedFromStaticMethod()
    {
        StoreOptions(opts =>
        {
            opts.RegisterDocumentType<Account>();
        });

        var queryPlan = await theSession.Query<Account>().Where(x => x.Id == AccountId.New("123").Value).ExplainAsync();
    }

    public record AccountId(string Value)
    {
        public static AccountId New(string value) => new AccountId(value);
    }
}
