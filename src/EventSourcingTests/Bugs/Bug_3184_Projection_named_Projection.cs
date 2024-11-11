using System;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_3184_Projection_named_Projection : BugIntegrationContext
{
    [Fact]
    public async Task codegen_works()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<AccountListInformation.Projection>(ProjectionLifecycle.Inline);
        });

        theSession.Events.Append(Guid.NewGuid(), new AccountCreated());
        await theSession.SaveChangesAsync();
    }
}

public class AccountCreated
{

}

public record AccountId(Guid Id);

public class AccountListInformation
{
    public Guid Id { get; set; }
    public AccountId AccountId { get; private set; }
    public string Name { get; private set; }
    public decimal Balance { get; private set; }

    public void Apply(AccountCreated accountCreated)
    {
        // Id = accountCreated.Id.Value;
        // AccountId = accountCreated.Id;
        // Name = accountCreated.Name;
        // Balance = 0;
    }

    public class Projection : SingleStreamProjection<AccountListInformation>
    {
        public Projection()
        {
            ProjectEvent<AccountCreated>((i, e) => i.Apply(e));
        }
    }
}
